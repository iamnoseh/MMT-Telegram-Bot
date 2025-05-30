using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using TelegramBot.Domain.DTOs;
using TelegramBot.Domain.Entities;
using TelegramBot.Services.OptionServices;
using TelegramBot.Services.QuestionService;
using TelegramBot.Services.SubjectService;
using TelegramBot.Services.UserResponceService;
using User = TelegramBot.Domain.Entities.User;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DataContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<IOptionService, OptionService>();
builder.Services.AddScoped<IResponseService, ResponseService>();
builder.Services.AddScoped<ISubjectService, SubjectService>();

await using var app = builder.Build();

// Apply migrations and seed subjects at startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataContext>();
    try
    {
        // Apply any pending migrations
        db.Database.Migrate();

        // Seed subjects if not already present
        SeedSubjects(db);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error during database migration or seeding: {ex.Message}");
    }
}

app.MapGet("/questions", async (DataContext context) =>
{
    var questions = await context.Questions.Include(q => q.Option).ToListAsync();
    return Results.Ok(questions);
});

var token = builder.Configuration["BotConfiguration:Token"]
            ?? throw new ArgumentNullException("Telegram Bot Token is not configured!");

var botHelper = new TelegramBotHelper(
    token,
    app.Services.GetRequiredService<IQuestionService>(),
    app.Services.GetRequiredService<IOptionService>(),
    app.Services.GetRequiredService<IResponseService>(),
    app.Services.GetRequiredService<ISubjectService>(),
    app.Services);

Task.Run(() => botHelper.StartBotAsync());

app.Run();

// Method to seed subjects into the database
void SeedSubjects(DataContext db)
{
    var subjects = new[]
    {
        new Subject { Id = 1, Name = "Химия" },
        new Subject { Id = 2, Name = "Биология" },
        new Subject { Id = 3, Name = "Забони тоҷикӣ" },
        new Subject { Id = 4, Name = "English" },
        new Subject { Id = 5, Name = "Таърих" }
    };

    foreach (var subject in subjects)
    {
        if (!db.Subjects.Any(s => s.Id == subject.Id))
        {
            db.Subjects.Add(subject);
        }
    }

    db.SaveChanges();
}

#region TelegramBotHelper

internal class TelegramBotHelper
{
    private readonly IQuestionService _questionService;
    private readonly IOptionService _optionService;
    private readonly IResponseService _responseService;
    private readonly ISubjectService _subjectService;
    private readonly TelegramBotClient _client;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _channelId;
    private readonly string _channelLink;
    
    private readonly Dictionary<long, RegistrationInfo> _pendingRegistrations = new();
    private readonly Dictionary<long, int> _userScores = new();
    private readonly Dictionary<long, int> _userQuestions = new();
    private readonly Dictionary<long, bool> _pendingBroadcast = new();
    private const int MaxQuestions = 10;
    private readonly Dictionary<long, int> _userCurrentSubject = new();

    public TelegramBotHelper(
        string token,
        IQuestionService questionService,
        IOptionService optionService,
        IResponseService responseService,
        ISubjectService subjectService,
        IServiceProvider serviceProvider)
    {
        _questionService = questionService;
        _optionService = optionService;
        _responseService = responseService;
        _subjectService = subjectService;
        _client = new TelegramBotClient(token);
        _serviceProvider = serviceProvider;
        
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        _channelId = configuration["TelegramChannel:ChannelId"] 
            ?? throw new ArgumentNullException("Channel ID is not configured!");
        _channelLink = configuration["TelegramChannel:ChannelLink"]
            ?? throw new ArgumentNullException("Channel Link is not configured!");
    }
    
    public async Task StartBotAsync()
    {
        try
        {
            var me = await _client.GetMeAsync();
            Console.WriteLine($"Bot connected as: {me.Username}");

            var offset = 0;
            while (true)
            {
                try
                {
                    var updates = await _client.GetUpdatesAsync(offset);
                    foreach (var update in updates)
                    {
                        await HandleUpdateAsync(update);
                        offset = update.Id + 1;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in polling: {ex.Message}");
                    await Task.Delay(1000);
                }
                await Task.Delay(500);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting bot: {ex.Message}");
        }
    }

    #region Update Handler 
    private async Task HandleUpdateAsync(Update update)
    {
        if (update.Type == UpdateType.Message && update.Message != null)
        {
            var message = update.Message;
            var chatId = message.Chat.Id;
            var text = message.Text;

            // Check if admin is sending a broadcast message
            if (_pendingBroadcast.ContainsKey(chatId) && _pendingBroadcast[chatId])
            {
                if (await IsUserAdminAsync(chatId))
                {
                    await HandleBroadcastMessageAsync(chatId, text);
                    return;
                }
                else
                {
                    _pendingBroadcast.Remove(chatId);
                    await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд паём фиристанд!");
                }
            }

            // Check for document upload
            if (message.Document != null)
            {
                if (await IsUserAdminAsync(chatId))
                {
                    if (!_userCurrentSubject.ContainsKey(chatId))
                    {
                        await _client.SendMessage(chatId,
                            "❌ Лутфан аввал фанро интихоб кунед!",
                            replyMarkup: await GetMainButtonsAsync(chatId));
                        return;
                    }
                    await HandleFileUploadAsync(message);
                }
                else
                {
                    await _client.SendMessage(chatId,
                        "❌ Танҳо админҳо метавонанд файл боргузорӣ кунанд!");
                }
                return;
            }

            // Allow only /start and /register without subscription
            if (text != "/start" && text != "/register")
            {
                if (!await CheckChannelSubscriptionAsync(chatId))
                {
                    return;
                }
            }

            if (message.Contact != null)
            {
                await HandleContactRegistrationAsync(message);
                return;
            }

            if (_pendingRegistrations.ContainsKey(chatId))
            {
                var reg = _pendingRegistrations[chatId];
                if (!reg.IsNameReceived)
                {
                    await HandleNameRegistrationAsync(chatId, text);
                    return;
                }
                else if (reg.IsNameReceived && !reg.IsCityReceived)
                {
                    await HandleCityRegistrationAsync(chatId, text);
                    return;
                }
            }

            switch (text)
            {
                case "/start":
                    if (!await IsUserRegisteredAsync(chatId))
                    {
                        await SendRegistrationRequestAsync(chatId);
                    }
                    else
                    {
                        await _client.SendMessage(chatId,
                            "Хуш омадед! Барои оғоз тугмаи 'Саволи нав'-ро пахш кунед.",
                            replyMarkup: await GetMainButtonsAsync(chatId));
                    }
                    break;

                case "/register":
                    if (!await IsUserRegisteredAsync(chatId))
                    {
                        await SendRegistrationRequestAsync(chatId);
                    }
                    else
                    {
                        await _client.SendMessage(chatId,
                            "Шумо аллакай сабт шудаед!");
                    }
                    break;

                case "❓ Саволи нав":
                    if (!await IsUserRegisteredAsync(chatId))
                    {
                        await _client.SendMessage(chatId,
                            "Лутфан аввал ба бот сабт шавед. Барои сабт /register-ро пахш кунед.");
                    }
                    else
                    {
                        await HandleNewQuestionAsync(chatId);
                    }
                    break;

                case "🏆 Топ":
                    await HandleTopCommandAsync(chatId);
                    break;

                case "👤 Профил":
                    await HandleProfileCommandAsync(chatId);
                    break;

                case "ℹ️ Кумак":
                    await HandleHelpCommandAsync(chatId);
                    break;

                case "📚 Интихоби фан":
                    var subjectKeyboard = new ReplyKeyboardMarkup
                    {
                        Keyboard = new List<List<KeyboardButton>>
                        {
                            new() { new KeyboardButton("🧪 Химия"), new KeyboardButton("🔬 Биология") },
                            new() { new KeyboardButton("📖 Забони тоҷикӣ"), new KeyboardButton("🌍 English") },
                            new() { new KeyboardButton("📜 Таърих") },
                            new() { new KeyboardButton("⬅️ Бозгашт") }
                        },
                        ResizeKeyboard = true
                    };
                    await _client.SendMessage(chatId,
                        "Лутфан фанро интихоб кунед:",
                        replyMarkup: subjectKeyboard);
                    break;

                case "🧪 Химия":
                case "🔬 Биология":
                case "📖 Забони тоҷикī":
                case "🌍 English":
                case "📜 Таърих":
                    await HandleSubjectSelectionAsync(chatId, text);
                    break;

                case "⬅️ Бозгашт":
                    await _client.SendMessage(chatId,
                        "Менюи асосī",
                        replyMarkup: await GetMainButtonsAsync(chatId));
                    break;

                case "👨‍💼 Админ":
                    await HandleAdminCommandAsync(chatId);
                    break;

                case "📢 Фиристодани паём":
                    if (await IsUserAdminAsync(chatId))
                    {
                        _pendingBroadcast[chatId] = true;
                        await _client.SendMessage(chatId,
                            "Лутфан паёми худро барои фиристодан ба ҳамаи корбарон ворид кунед:",
                            replyMarkup: new ReplyKeyboardRemove());
                    }
                    else
                    {
                        await _client.SendMessage(chatId,
                            "❌ Танҳо админҳо метавонанд паём фиристанд!");
                    }
                    break;

                case "📊 Омор":
                    if (await IsUserAdminAsync(chatId))
                    {
                        await HandleStatisticsCommandAsync(chatId);
                    }
                    else
                    {
                        await _client.SendMessage(chatId,
                            "❌ Танҳо админҳо метавонанд оморро бубинанд!");
                    }
                    break;

                case "📝 Саволҳо":
                    if (await IsUserAdminAsync(chatId))
                    {
                        await _client.SendMessage(chatId,
                            "Функсияи 'Саволҳо' ҳанӯз амалӣ нашудааст.",
                            replyMarkup: await GetAdminButtonsAsync());
                    }
                    else
                    {
                        await _client.SendMessage(chatId,
                            "❌ Танҳо админҳо метавонанд саволҳоро бубинанд!");
                    }
                    break;

                default:
                    await _client.SendMessage(chatId, "Фармони нодуруст!");
                    break;
            }
        }
        else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            await HandleCallbackQueryAsync(update.CallbackQuery);
        }
    }

    #endregion

    #region Registration Methods

    private async Task<bool> IsUserRegisteredAsync(long chatId)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        return await dbContext.Users.AnyAsync(u => u.ChatId == chatId);
    }

    private async Task SendRegistrationRequestAsync(long chatId)
    {
        var requestContactButton = new KeyboardButton("Telephone Number") { RequestContact = true };
        var keyboard = new ReplyKeyboardMarkup(new List<KeyboardButton> { requestContactButton })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        await _client.SendMessage(chatId,
            "Барои сабт кардан, лутфан тугмаи зерро пахш кунед!",
            replyMarkup: keyboard);
    }

    private async Task HandleContactRegistrationAsync(Message message)
    {
        var chatId = message.Chat.Id;
        var contact = message.Contact;

        var autoUsername = !string.IsNullOrWhiteSpace(message.Chat.Username)
            ? message.Chat.Username
            : message.Chat.FirstName;

        if (!_pendingRegistrations.ContainsKey(chatId))
        {
            _pendingRegistrations[chatId] = new RegistrationInfo
            {
                Contact = contact,
                AutoUsername = autoUsername,
                IsNameReceived = false,
                IsCityReceived = false
            };

            await _client.SendMessage(chatId,
                "Ташаккур! Акнун, лутфан номи худро ворид кунед.",
                replyMarkup: new ReplyKeyboardRemove());
        }
        else
        {
            await _client.SendMessage(chatId,
                "Лутфан номи худро ворид кунед, то сабт ба итмом расад.");
        }
    }

    private async Task HandleNameRegistrationAsync(long chatId, string name)
    {
        if (!_pendingRegistrations.ContainsKey(chatId))
            return;

        var regInfo = _pendingRegistrations[chatId];
        regInfo.Name = name;
        regInfo.IsNameReceived = true;

        await _client.SendMessage(chatId,
            "Лутфан шаҳри худро ворид кунед.",
            replyMarkup: new ReplyKeyboardRemove());
    }

    private async Task HandleCityRegistrationAsync(long chatId, string city)
    {
        if (!_pendingRegistrations.ContainsKey(chatId))
            return;

        var regInfo = _pendingRegistrations[chatId];
        regInfo.City = city;
        regInfo.IsCityReceived = true;

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();

            var user = new User
            {
                ChatId = chatId,
                Username = regInfo.AutoUsername,
                Name = regInfo.Name,
                PhoneNumber = regInfo.Contact.PhoneNumber,
                City = regInfo.City,
                Score = 0
            };

            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync();

            await _client.SendMessage(chatId,
                "Сабти шумо бо муваффақият анҷом ёфт!\nБарои оғоз тугмаи 'Саволи нав'-ро пахш кунед!",
                replyMarkup: await GetMainButtonsAsync(chatId));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving user registration: {ex.Message}");
            await _client.SendMessage(chatId,
                "Дар сабти маълумот хатое рūй дод. Лутфан баъдтар кūшиш намоед.");
        }
        finally
        {
            _pendingRegistrations.Remove(chatId);
        }
    }

    #endregion

    #region Question Methods      
    private async Task<IReplyMarkup> GetMainButtonsAsync(long chatId)
    {
        var isAdmin = await IsUserAdminAsync(chatId);
        var buttons = new List<List<KeyboardButton>>();
        
        buttons.Add(new() { new KeyboardButton("📚 Интихоби фан"), new KeyboardButton("❓ Саволи нав") });
        buttons.Add(new() { new KeyboardButton("🏆 Топ"), new KeyboardButton("👤 Профил") });
        
        if (isAdmin)
        {
            buttons.Add(new() { new KeyboardButton("ℹ️ Кумак"), new KeyboardButton("👨‍💼 Админ") });
        }
        else
        {
            buttons.Add(new() { new KeyboardButton("ℹ️ Кумак") });
        }

        return new ReplyKeyboardMarkup(buttons) { ResizeKeyboard = true };
    }

    private IReplyMarkup GetButtons(int questionId)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("A", $"{questionId}_A"),
                InlineKeyboardButton.WithCallbackData("B", $"{questionId}_B")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("C", $"{questionId}_C"),
                InlineKeyboardButton.WithCallbackData("D", $"{questionId}_D")
            }
        });
    }

    private async Task HandleSubjectSelectionAsync(long chatId, string text)
    {
        int subjectId = text switch
        {
            "🧪 Химия" => 1,
            "🔬 Биология" => 2,
            "📖 Забони тоҷикī" => 3,
            "🌍 English" => 4,
            "📜 Таърих" => 5,
            _ => 0
        };

        if (subjectId == 0)
            return;

        _userCurrentSubject[chatId] = subjectId;
        
        var isAdmin = await IsUserAdminAsync(chatId);
        var buttons = new List<List<KeyboardButton>>();
        string message;
        
        if (isAdmin)
        {
            buttons.Add(new() { new KeyboardButton("📤 Боргузории файл") });
            message = $"Шумо фанни {text}-ро интихоб кардед.\n" +
                     "Барои илова кардани саволҳо файли .docx-ро равон кунед.";
        }
        else
        {
            message = $"Шумо фанни {text}-ро интихоб кардед.\n" +
                     "Барои оғози тест тугмаи 'Саволи нав'-ро пахш кунед.";
        }
        
        buttons.Add(new() { new KeyboardButton("⬅️ Бозгашт") });

        var keyboard = new ReplyKeyboardMarkup(buttons)
        {
            ResizeKeyboard = true
        };

        await _client.SendMessage(chatId, message, replyMarkup: keyboard);
    }

    private async Task HandleNewQuestionAsync(long chatId)
    {
        if (!_userCurrentSubject.TryGetValue(chatId, out int currentSubject))
        {
            await _client.SendMessage(chatId,
                "❌ Лутфан аввал фанро интихоб кунед!",
                replyMarkup: await GetMainButtonsAsync(chatId));
            return;
        }

        if (!_userQuestions.ContainsKey(chatId))
        {
            _userQuestions[chatId] = 0;
            _userScores[chatId] = 0;
        }

        if (_userQuestions[chatId] >= MaxQuestions)
        {
            string res;
            if (_userScores[chatId] == MaxQuestions)
            {
                res = $"🎉 Офарин! Шумо 100% холҳоро соҳиб шудед!\n" +
                      $"Холҳои шумо: {_userScores[chatId]}/{MaxQuestions}.";
            }
            else
            {
                res = $"📝 Тест ба охир расид!\n" +
                      $"Холҳои шумо: {_userScores[chatId]}/{MaxQuestions}.\n" +
                      $"♻️ Аз нав кӯшиш кунед!";
            }

            await _client.SendMessage(chatId, res,
                replyMarkup: new InlineKeyboardMarkup(
                    InlineKeyboardButton.WithCallbackData("️♻️ Аз нав оғоз кардан!", "restart")));
            return;
        }

        var question = await _questionService.GetRandomQuestionBySubject(currentSubject);
        if (question != null)
        {
            _userQuestions[chatId]++;
            await _client.SendMessage(chatId,
                $"📚 Фан: {question.SubjectName}\n\n" +
                $"❓ {question.QuestionText}\n\n" +
                $"A) {question.FirstOption}\n" +
                $"B) {question.SecondOption}\n" +
                $"C) {question.ThirdOption}\n" +
                $"D) {question.FourthOption}",
                replyMarkup: GetButtons(question.QuestionId));
        }
        else
        {
            await _client.SendMessage(chatId,
                "❌ Дар айни замон саволҳо барои ин фан дастрас нестанд.");
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery)
    {
        if (callbackQuery.Message?.Chat == null || callbackQuery.Data == null)
            return;

        var chatId = callbackQuery.Message.Chat.Id;
        var messageId = callbackQuery.Message.MessageId;

        if (callbackQuery.Data == "check_subscription")
        {
            if (await IsUserChannelMemberAsync(chatId))
            {
                await _client.DeleteMessageAsync(chatId, messageId);
                await _client.SendMessage(chatId, 
                    "✅ Ташаккур барои обуна! Акнун шумо метавонед аз бот истифода баред.");
                return;
            }
            else
            {
                await _client.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "❌ Шумо ҳоло ба канал обуна нашудаед!",
                    showAlert: true
                );
                return;
            }
        }

        var callbackData = callbackQuery.Data.Split('_');

        if (callbackQuery.Data == "restart")
        {
            _userScores[chatId] = 0;
            _userQuestions[chatId] = 0;

            await _client.EditMessageTextAsync(chatId, messageId,
                "Барои тест омодаед? Барои оғоз \"Саволи нав\"-ро пахш кунед.");
            return;
        }

        if (!int.TryParse(callbackData[0], out int questionId))
            return;

        var question = await _questionService.GetQuestionById(questionId);
        if (question == null)
        {
            await _client.EditMessageTextAsync(chatId, messageId, "Савол ёфт нашуд.");
            return;
        }

        if (!_userScores.ContainsKey(chatId))
        {
            _userScores[chatId] = 0;
        }

        var selectedOption = callbackData[1].Trim().ToUpper();
        string selectedOptionText = selectedOption switch
        {
            "A" => question.FirstOption.Trim(),
            "B" => question.SecondOption.Trim(),
            "C" => question.ThirdOption.Trim(),
            "D" => question.FourthOption.Trim(),
            _ => ""
        };

        string correctAnswer = question.Answer.Trim();
        bool isCorrect = selectedOptionText == correctAnswer;

        if (isCorrect)
        {
            _userScores[chatId]++;
            await _client.EditMessageTextAsync(chatId, messageId, "Офарин! +1 балл");

            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId);
            if (user != null)
            {
                user.Score += 1;
                await dbContext.SaveChangesAsync();
            }
        }
        else
        {
            await _client.EditMessageTextAsync(chatId, messageId,
                $"❌ Афсūс! Ҷавоби шумо нодуруст!\n" +
                $"💡 Ҷавоби дуруст: {correctAnswer} буд.");
        }

        var userResponse = new UserResponse
        {
            ChatId = chatId,
            QuestionId = questionId,
            SelectedOption = selectedOptionText,
            IsCorrect = isCorrect
        };
        await _responseService.SaveUserResponse(userResponse);
    }

    private async Task<bool> IsUserChannelMemberAsync(long chatId)
    {
        try
        {
            var chatMember = await _client.GetChatMemberAsync(_channelId, chatId);
            var isValid = chatMember.Status is ChatMemberStatus.Member 
                         or ChatMemberStatus.Administrator 
                         or ChatMemberStatus.Creator;
            
            Console.WriteLine($"Checking subscription for user {chatId}: Status={chatMember.Status}, IsValid={isValid}");
            return isValid;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking channel membership: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> CheckChannelSubscriptionAsync(long chatId)
    {
        if (!await IsUserChannelMemberAsync(chatId))
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new []
                {
                    InlineKeyboardButton.WithUrl("Обуна шудан ба канал", _channelLink)
                },
                new []
                {
                    InlineKeyboardButton.WithCallbackData("🔄 Тафтиш", "check_subscription")
                }
            });

            await _client.SendMessage(chatId,
                "⚠️ Барои истифодаи бот, лутфан аввал ба канали мо обуна шавед!",
                replyMarkup: keyboard);
            return false;
        }
        return true;
    }

    #endregion

    #region Top & Profile & Help

    private async Task HandleTopCommandAsync(long chatId)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var topUsers = await dbContext.Users.OrderByDescending(u => u.Score).Take(50).ToListAsync();

        if (topUsers.Count == 0)
        {
            await _client.SendMessage(chatId, "Лист холī аст!");
            return;
        }

        string GetLevelStars(int level)
        {
            return new string('⭐', level);
        }

        string GetRankColor(int rank)
        {
            return rank switch
            {
                1 => "🥇", // Зард (тилло)
                2 => "🥈", // Нуқра
                3 => "🥉", // Биринҷī
                <= 10 => "🔹", // Кабуд
                _ => "⚪" // Сафед (бе ранг)
            };
        }

        int cnt = 0;
        var messageText = "<b>🏆 Топ 50 Беҳтаринҳо</b>\n\n"
                          + "<pre>#        Ном ва Насаб         Хол  </pre>\n"
                          + "<pre>----------------------------------</pre>\n";

        foreach (var user in topUsers)
        {
            cnt++;
            if (user.Name.Length > 15)
            {
                user.Name = user.Name[..15] + "...";
            }
            string levelStars = GetLevelStars(GetLevel(user.Score));
            string rankSymbol = GetRankColor(cnt);
            messageText += $"<pre>{cnt,0}.{rankSymbol} {user.Name,-20} |{user.Score,-0}|{rankSymbol,2}</pre>\n";
        }
        
        await _client.SendMessage(chatId, messageText, parseMode: ParseMode.Html);
    }

    private async Task HandleProfileCommandAsync(long chatId)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId);
        if (user != null)
        {
            int level = GetLevel(user.Score);
            string profileText = $"Profile:\n    {user.Name}\n" +
                                 $"Шаҳр: {user.City}\n" +
                                 $"Score: {user.Score}\n" +
                                 $"Левел: {level}";
            await _client.SendMessage(chatId, profileText);
        }
        else
        {
            await _client.SendMessage(chatId,
                "Шумо ҳанūз сабт нашудаед. Лутфан барои сабт /register-ро пахш кунед.");
        }
    }

    private async Task HandleHelpCommandAsync(long chatId)
    {
        string helpText = "Дастурҳо:\n" +
                          "/start - оғоз ва санҷиши сабт шудан\n" +
                          "/register - сабт кардани ҳисоби корбар\n" +
                          "Саволи нав - барои гирифтани савол\n" +
                          "Top - барои дидани топ 50 корбар\n" +
                          "Profile - барои дидани маълумоти шахсии шумо\n" +
                          "Help - барои дидани ин рūйхат\n";
        await _client.SendMessage(chatId, helpText);
    }

    private int GetLevel(int score)
    {
        if (score <= 150)
            return 1;
        else if (score <= 300)
            return 2;
        else if (score <= 450)
            return 3;
        else if (score <= 600)
            return 4;
        else
            return 5;
    }

    #endregion

    #region Broadcast Message

    private async Task HandleBroadcastMessageAsync(long chatId, string messageText)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            await _client.SendMessage(chatId,
                "❌ Паём набояд холī бошад! Лутфан паёми дигар ворид кунед.");
            return;
        }

        _pendingBroadcast.Remove(chatId);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
            var users = await dbContext.Users.Select(u => u.ChatId).ToListAsync();

            int sentCount = 0;
            foreach (var userChatId in users)
            {
                try
                {
                    await _client.SendMessage(userChatId,
                        $"📢 Паёми муҳим:\n{messageText}");
                    sentCount++;
                    await Task.Delay(50); // Avoid Telegram rate limits
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending message to user {userChatId}: {ex.Message}");
                }
            }

            await _client.SendMessage(chatId,
                $"✅ Паём бо муваффақият ба {sentCount} корбар фиристода шуд!",
                replyMarkup: await GetAdminButtonsAsync());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error broadcasting message: {ex.Message}");
            await _client.SendMessage(chatId,
                "❌ Хатогī ҳангоми фиристодани паём ба корбарон. Лутфан боз кūшиш кунед.",
                replyMarkup: await GetAdminButtonsAsync());
        }
    }

    #endregion

    #region Statistics

    private async Task HandleStatisticsCommandAsync(long chatId)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();

            // Count active users
            var activeUsersCount = await dbContext.Users.CountAsync();

            // Count questions per subject
            var questionCounts = await dbContext.Questions
                .GroupBy(q => q.SubjectId)
                .Select(g => new { SubjectId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.SubjectId, g => g.Count);

            // Get subject names
            var subjects = await dbContext.Subjects.ToListAsync();
            var subjectStats = subjects.Select(s => 
                $"{s.Name}: {(questionCounts.TryGetValue(s.Id, out int count) ? count : 0)} савол").ToList();

            // Format statistics message
            var statsMessage = "<b>📊 Омор</b>\n\n" +
                              $"👥 <b>Корбарони фаъол</b>: {activeUsersCount} нафар\n" +
                              $"\n📚 <b>Миқдори саволҳо аз рӯи фанҳо</b>:\n" +
                              string.Join("\n", subjectStats);

            await _client.SendMessage(chatId, statsMessage, 
                parseMode: ParseMode.Html, 
                replyMarkup: await GetAdminButtonsAsync());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving statistics: {ex.Message}");
            await _client.SendMessage(chatId,
                "❌ Хатогī ҳангоми гирифтани омор. Лутфан боз кūшиш кунед.",
                replyMarkup: await GetAdminButtonsAsync());
        }
    }

    #endregion

    #region File Upload

    private async Task HandleFileUploadAsync(Message message)
    {
        if (message.Document == null)
            return;

        var chatId = message.Chat.Id;
        var fileName = message.Document.FileName ?? "unnamed.docx";
        var username = !string.IsNullOrWhiteSpace(message.From?.Username) 
            ? $"@{message.From.Username}" 
            : message.From?.FirstName ?? "Unknown user";

        if (!fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            await _client.SendMessage(chatId, 
                "❌ Лутфан танҳо файли .docx равон кунед!");
            return;
        }

        try
        {
            var file = await _client.GetFileAsync(message.Document.FileId);
            if (file.FilePath == null)
            {
                throw new Exception("Could not get file path from Telegram");
            }

            using var stream = new MemoryStream();
            await _client.DownloadFile(file.FilePath, stream);
            stream.Position = 0;

            if (!_userCurrentSubject.TryGetValue(chatId, out int currentSubject))
            {
                await _client.SendMessage(chatId, 
                    "❌ Лутфан аввал фанро интихоб кунед!");
                return;
            }

            await NotifyAdminsAsync($"📥 Файли нав аз {username}\nНоми файл: {fileName}\nБа коркард дода шуд...");

            var questions = ParseQuestionsFromDocx(stream, currentSubject);

            foreach (var question in questions)
            {
                await _questionService.CreateQuestion(question);
            }

            var successMessage = $"✅ {questions.Count} савол бо муваффақият илова карда шуд!";
            await _client.SendMessage(chatId, successMessage);

            await NotifyAdminsAsync($"✅ Аз файли {fileName}\n" +
                                   $"Ки аз тарафи {username} фиристода шуда буд,\n" +
                                   $"{questions.Count} савол бо муваффақият илова карда шуд!");
        }
        catch (Exception ex)
        {
            var errorMessage = $"❌ Хатогī: {ex.Message}";
            await _client.SendMessage(chatId, errorMessage);

            await NotifyAdminsAsync($"❌ Хатогī ҳангоми коркарди файл:\n" +
                                   $"Файл: {fileName}\n" +
                                   $"Корбар: {username}\n" +
                                   $"Хатогī: {ex.Message}");
        }
    }

    private async Task NotifyAdminsAsync(string message)
    {
        try
        {
            var chatMembers = await _client.GetChatAdministratorsAsync(_channelId);
            
            foreach (var member in chatMembers)
            {
                if (member.Status is ChatMemberStatus.Creator or ChatMemberStatus.Administrator)
                {
                    try
                    {
                        await _client.SendMessage(member.User.Id, message);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error notifying admins: {ex.Message}");
        }
    }

    private List<QuestionDTO> ParseQuestionsFromDocx(Stream docxStream, int subjectId)
    {
        var questions = new List<QuestionDTO>();

        using (var wordDoc = WordprocessingDocument.Open(docxStream, false))
        {
            if (wordDoc.MainDocumentPart?.Document?.Body == null)
            {
                throw new Exception("Файл холī аст ё шакли нодуруст дорад");
            }

            var body = wordDoc.MainDocumentPart.Document.Body;
            var paragraphs = body.Elements<Paragraph>()
                               .Where(p => p.InnerText != null)
                               .Select(p => p.InnerText)
                               .Where(text => !string.IsNullOrWhiteSpace(text))
                               .ToList();
            
            if (paragraphs.Count < 5)
            {
                throw new Exception("Файл холī аст ё саволҳо нодуруст ворид шудаанд");
            }

            for (int i = 0; i <= paragraphs.Count - 5; i += 5)
            {
                string questionText = paragraphs[i].Trim();

                string[] variants = new string[4];
                string correctAnswer = "";
                for (int j = 0; j < 4; j++)
                {
                    string line = paragraphs[i + j + 1].Trim();
                    int idx = line.IndexOf(")");
                    if (idx >= 0)
                    {
                        line = line.Substring(idx + 1).Trim();
                    }
                    if (line.EndsWith("--"))
                    {
                        line = line.Substring(0, line.Length - 2).Trim();
                        correctAnswer = line;
                    }
                    variants[j] = line;
                }

                if (string.IsNullOrEmpty(correctAnswer))
                    throw new Exception($"Ҷавоби дуруст барои савол '{questionText}' ёфт нашуд.");

                var questionDto = new QuestionDTO
                {
                    QuestionText = questionText,
                    SubjectId = subjectId,
                    OptionA = variants[0],
                    OptionB = variants[1],
                    OptionC = variants[2],
                    OptionD = variants[3],
                    CorrectAnswer = correctAnswer
                };

                questions.Add(questionDto);
            }
        }

        if (questions.Count == 0)
        {
            throw new Exception("Дар файл ягон савол ёфт нашуд");
        }

        return questions;
    }

    #endregion

    #region Admin Panel      
    private async Task<bool> IsUserAdminAsync(long chatId)
    {
        try
        {
            var chatMember = await _client.GetChatMemberAsync(_channelId, chatId);
            var isAdmin = chatMember.Status is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;
            Console.WriteLine($"Checking admin status for ChatId {chatId}: Status={chatMember.Status}, IsAdmin={isAdmin}");
            return isAdmin;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking admin status: {ex.Message}");
            return false;
        }
    }
    
    private async Task<IReplyMarkup> GetAdminButtonsAsync()
    {
        var adminKeyboard = new ReplyKeyboardMarkup
        {   
            Keyboard = new List<List<KeyboardButton>>
            {
                new() { new KeyboardButton("📚 Интихоби фан") },
                new() { new KeyboardButton("📊 Омор"), new KeyboardButton("📝 Саволҳо") },
                new() { new KeyboardButton("📢 Фиристодани паём") },
                new() { new KeyboardButton("⬅️ Бозгашт") }
            },
            ResizeKeyboard = true
        };
        return adminKeyboard;
    }
    
    private async Task HandleAdminCommandAsync(long chatId)
    {
        var isAdmin = await IsUserAdminAsync(chatId);
        Console.WriteLine($"Admin command requested by ChatId {chatId}: IsAdmin={isAdmin}");
        
        if (!isAdmin)
        {
            await _client.SendMessage(chatId,
                "❌ Бубахшед, шумо админ нестед! \nБарои админ шудан лутфан ба канал ҳамчун маъмур (админ) ё созанда (криейтор) илова шавед.");
            return;
        }
        
        await _client.SendMessage(chatId,
            "Хуш омадед ба панели админ!\n" +
            "Лутфан амалро интихоб кунед:",
            replyMarkup: await GetAdminButtonsAsync());
    }

    #endregion
}

#endregion