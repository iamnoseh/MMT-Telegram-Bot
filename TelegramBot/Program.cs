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
using TelegramBot.Services.QuestionServise;
using TelegramBot.Services.UserResponceService;
using User = TelegramBot.Domain.Entities.User;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<DataContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IQuestionService, QuestionService>();
builder.Services.AddScoped<IOptionService, OptionService>();
builder.Services.AddScoped<IResponseService, ResponseService>();

await using var app = builder.Build();


using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataContext>();
    db.Database.Migrate();
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
    app.Services);

Task.Run(() => botHelper.StartBotAsync());

app.Run();

#region TelegramBotHelper

internal class TelegramBotHelper
{
    private readonly IOptionService _optionService;
    private readonly IQuestionService _service;
    private readonly IResponseService _responseService;
    private readonly TelegramBotClient _client;
    private readonly IServiceProvider _serviceProvider;
    private readonly string _channelId;
    private readonly string _channelLink;
    
    private readonly Dictionary<long, RegistrationInfo> _pendingRegistrations = new();

    // Барои идора кардани саволҳо
    private readonly Dictionary<long, int> _userScores = new();
    private readonly Dictionary<long, int> _userQuestions = new();
    private const int MaxQuestions = 10;

    public TelegramBotHelper(
        string token,
        IQuestionService questionService,
        IOptionService optionService,
        IResponseService responseService,
        IServiceProvider serviceProvider)
    {
        _service = questionService;
        _optionService = optionService;
        _responseService = responseService;
        _client = new TelegramBotClient(token);
        _serviceProvider = serviceProvider;
        
        // Гирифтани маълумоти канал аз конфигуратсия
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
                    // Паёмҳоро бо offset мегирем
                    var updates = await _client.GetUpdatesAsync(offset);
                    foreach (var update in updates)
                    {
                        // Ҳар як update-ро коркард мекунем
                        await HandleUpdateAsync(update);

                        // offset-ро як адад зиёд мекунем, то паёмҳои такрорӣ нагирем
                        offset = update.Id + 1;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in polling: {ex.Message}");
                    // Барои пешгирӣ аз даври бепоён андаке таъхир
                    await Task.Delay(1000);
                }
                // Барои паст кардани сарборӣ андаке таъхир
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
            var chatId = update.Message.Chat.Id;
            var text = update.Message.Text;

            // Иҷозат додани танҳо фармонҳои /start ва /register бе обуна
            if (text != "/start" && text != "/register")
            {
                if (!await CheckChannelSubscription(chatId))
                {
                    return;
                }
            }

            if (update.Message.Contact != null)
            {
                await HandleContactRegistration(update.Message);
                return;
            }

            if (_pendingRegistrations.ContainsKey(chatId))
            {
                var reg = _pendingRegistrations[chatId];
                if (!reg.IsNameReceived)
                {
                    await HandleNameRegistration(chatId, text);
                    return;
                }
                else if (reg.IsNameReceived && !reg.IsCityReceived)
                {
                    await HandleCityRegistration(chatId, text);
                    return;
                }
            }

            switch (text)
            {
                case "/start":
                    if (!await IsUserRegistered(chatId))
                    {
                        await SendRegistrationRequest(chatId);
                    }
                    else
                    {
                        await _client.SendTextMessageAsync(chatId,
                            "Хуш омадед! Барои оғоз тугмаи 'Саволи нав'-ро пахш кунед.",
                            replyMarkup: GetMainButtons());
                    }
                    break;

                case "/register":
                    if (!await IsUserRegistered(chatId))
                    {
                        await SendRegistrationRequest(chatId);
                    }
                    else
                    {
                        await _client.SendTextMessageAsync(chatId,
                            "Шумо аллакай сабт шудаед!");
                    }
                    break;

                case "❓ Саволи нав":
                    if (!await IsUserRegistered(chatId))
                    {
                        await _client.SendTextMessageAsync(chatId,
                            "Лутфан аввал ба бот сабт шавед. Барои сабт /register-ро пахш кунед.");
                    }
                    else
                    {
                        await HandleNewQuestion(chatId);
                    }
                    break;

                case "🏆 Топ":
                    await HandleTopCommand(chatId);
                    break;

                case "👤 Профил":
                    await HandleProfileCommand(chatId);
                    break;

                case "ℹ️ Кумак":
                    await HandleHelpCommand(chatId);
                    break;

                default:
                    // Агар ягон фармони дигар ворид шуда бошад, аммо корбар дар раванди сабт нест
                    await _client.SendMessage(chatId, "Фармони нодуруст !");
                    break;
            }
        }
        else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            await HandleCallbackQuery(update.CallbackQuery);
        }
    }

    #endregion

    #region Registration Methods

    private async Task<bool> IsUserRegistered(long chatId)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        return await dbContext.Users.AnyAsync(u => u.ChatId == chatId);
    }

    private async Task SendRegistrationRequest(long chatId)
    {
        var requestContactButton = new KeyboardButton("Telephone Number") { RequestContact = true };
        var keyboard = new ReplyKeyboardMarkup(new List<KeyboardButton> { requestContactButton })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        await _client.SendTextMessageAsync(chatId,
            "Барои сабт кардан, лутфан тугмаи зерpo пахш кунед !",
            replyMarkup: keyboard);
    }

    private async Task HandleContactRegistration(Message message)
    {
        var chatId = message.Chat.Id;
        var contact = message.Contact;

        // Агар message.Chat.Username холӣ бошад, аз FirstName истифода мекунем.
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

            await _client.SendTextMessageAsync(chatId,
                "Ташаккур! Акнун, лутфан номи худро ворид кунед.",
                replyMarkup: new ReplyKeyboardRemove());
        }
        else
        {
            await _client.SendTextMessageAsync(chatId,
                "Лутфан номи худро ворид кунед, то сабт ба итмом расад.");
        }
    }

    private async Task HandleNameRegistration(long chatId, string name)
    {
        if (!_pendingRegistrations.ContainsKey(chatId))
            return;

        var regInfo = _pendingRegistrations[chatId];
        regInfo.Name = name;
        regInfo.IsNameReceived = true;

        await _client.SendTextMessageAsync(chatId,
            "Лутфан шаҳри худро ворид кунед.",
            replyMarkup: new ReplyKeyboardRemove());
    }

    private async Task HandleCityRegistration(long chatId, string city)
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

            await _client.SendTextMessageAsync(chatId,
                "Сабти шумо бо муваффақият анҷом ёфт!\n'Барои оғоз тугмаи 'Саволи нав'-ро пахш кунед!",
                replyMarkup: GetMainButtons());
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving user registration: {ex.Message}");
            await _client.SendTextMessageAsync(chatId,
                "Дар сабти маълумот хатое рӯй дод. Лутфан баъдтар кӯшиш намоед.");
        }
        finally
        {
            _pendingRegistrations.Remove(chatId);
        }
    }

    #endregion

    #region Question Methods

    private IReplyMarkup GetMainButtons()
    {
        return new ReplyKeyboardMarkup
        {
            Keyboard = new List<List<KeyboardButton>>
            {
                new() { new KeyboardButton("❓ Саволи нав"), new KeyboardButton( "🏆 Топ") },
                new() { new KeyboardButton("👤 Профил"), new KeyboardButton("ℹ️ Кумак") }
            },
            ResizeKeyboard = true
        };
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

    private async Task HandleNewQuestion(long chatId)
    {
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
                res = $"Офарин! Шумо 100% холҳоро соҳиб шудед!\n" +
                      $"Холҳои шумо: {_userScores[chatId]}/{MaxQuestions}.";
            }
            else
            {
                res = $"Тест ба охир расид!\n" +
                      $"Холҳои шумо: {_userScores[chatId]}/{MaxQuestions}.\n" +
                      $"♻️ Аз нав кӯшиш кунед!";
            }

            await _client.SendTextMessageAsync(chatId, res,
                replyMarkup: new InlineKeyboardMarkup(
                    InlineKeyboardButton.WithCallbackData("️♻️ Аз нав оғоз кардан!", "restart")));
            return;
        }

        var question = await _service.GetQuestionWithOptionsDTO();
        if (question != null)
        {
            _userQuestions[chatId]++;
            await _client.SendTextMessageAsync(chatId,
                $"{question.QuestionText}\nA) {question.FirstOption}\nB) {question.SecondOption}\n" +
                $"C) {question.ThirdOption}\nD) {question.FourthOption}",
                replyMarkup: GetButtons(question.QuestionId));
        }
        else
        {
            await _client.SendTextMessageAsync(chatId, "Дар айни замон саволҳо дастрас нестанд.");
        }
    }    private async Task HandleCallbackQuery(CallbackQuery callbackQuery)
    {
        var chatId = callbackQuery.Message.Chat.Id;
        var messageId = callbackQuery.Message.MessageId;

        if (callbackQuery.Data == "check_subscription")
        {
            if (await IsUserChannelMember(chatId))
            {
                await _client.DeleteMessageAsync(chatId, messageId);
                await _client.SendTextMessageAsync(chatId, 
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

        var question = await _service.GetQuestionById(questionId);
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
        string correctAnswer = question.Answer.Trim();

        bool isCorrect = selectedOption switch
        {
            "A" => correctAnswer == question.FirstOption.Trim(),
            "B" => correctAnswer == question.SecondOption.Trim(),
            "C" => correctAnswer == question.ThirdOption.Trim(),
            "D" => correctAnswer == question.FourthOption.Trim(),
            _ => false
        };

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
        {            await _client.EditMessageTextAsync(chatId, messageId,
                $"❌ Афсӯс! Ҷавоби шумо нодуруст!\n" +
                $"💡 Ҷавоби дуруст: {question.Answer} буд.");
        }

        var userResponse = new UserResponse
        {
            ChatId = chatId,
            QuestionId = questionId,
            SelectedOption = selectedOption,
            IsCorrect = isCorrect
        };
        await _responseService.SaveUserResponse(userResponse);
    }    private async Task<bool> IsUserChannelMember(long chatId)
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

    private async Task<bool> CheckChannelSubscription(long chatId)
    {
        if (!await IsUserChannelMember(chatId))
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

            await _client.SendTextMessageAsync(chatId,
                "⚠️ Барои истифодаи бот, лутфан аввал ба канали мо обуна шавед!",
                replyMarkup: keyboard);
            return false;
        }
        return true;
    }

    #endregion

    #region Top & Profile & Help

    private async Task HandleTopCommand(long chatId)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var topUsers = await dbContext.Users.OrderByDescending(u => u.Score).Take(50).ToListAsync();

        if (topUsers.Count == 0)
        {
            await _client.SendTextMessageAsync(chatId, "Лист холӣ аст!");
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
                3 => "🥉", // Биринҷӣ
                <= 10 => "🔹", // Кабуд
                _ => "⚪" // Сафед (бе ранг)
            };
        }

        int cnt = 0;
        var messageText = "<b>🏆 Топ 50 Беҳтаринҳо</b>\n\n"
                          + "<b>📊 Рӯйхат:</b>\n"
                          + "<pre>#        Номy Насаб         Хол  </pre>\n"
                          + "<pre>----------------------------------</pre>\n";

        foreach (var user in topUsers)
        {
            cnt++;
            string levelStars = GetLevelStars(GetLevel(user.Score));
            string rankSymbol = GetRankColor(cnt);
            messageText += $"<pre>{cnt,0}.{rankSymbol} {user.Name,-12} |{user.Score,-0}|{rankSymbol}</pre>\n";
        }
        
        await _client.SendTextMessageAsync(chatId, messageText, parseMode: ParseMode.Html);

    }

    private async Task HandleProfileCommand(long chatId)
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
            await _client.SendTextMessageAsync(chatId, profileText);
        }
        else
        {
            await _client.SendTextMessageAsync(chatId,
                "Шумо ҳанӯз сабт нашудаед. Лутфан барои сабт /register-ро пахш кунед.");
        }
    }

    private async Task HandleHelpCommand(long chatId)
    {
        string helpText = "Дастурҳо:\n" +
                          "/start - оғоз ва санҷиши сабт шудан\n" +
                          "/register - сабт кардани ҳисоби корбар\n" +
                          "Саволи нав - барои гирифтани савол\n" +
                          "Top - барои дидани топ 50 корбар\n" +
                          "Profile - барои дидани маълумоти шахсии шумо\n" +
                          "Help - барои дидани ин рӯйхат\n";
        await _client.SendTextMessageAsync(chatId, helpText);
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
}

#endregion