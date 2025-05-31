using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
using Microsoft.EntityFrameworkCore;

namespace TelegramBot.Services;

public class TelegramBotHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly TelegramBotClient _client;
    private readonly string _channelId;
    private readonly string _channelLink;
    private readonly Dictionary<long, RegistrationInfo> _pendingRegistrations = new();
    private readonly Dictionary<long, int> _userScores = new();
    private readonly Dictionary<long, int> _userQuestions = new();
    private readonly Dictionary<long, bool> _pendingBroadcast = new();
    private readonly Dictionary<long, int> _userCurrentSubject = new();
    private const int MaxQuestions = 10;

    public TelegramBotHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        var token = configuration["BotConfiguration:Token"]
            ?? throw new ArgumentNullException("Telegram Bot Token is not configured!");
        _client = new TelegramBotClient(token);
        _channelId = configuration["TelegramChannel:ChannelId"]
            ?? throw new ArgumentNullException("Channel ID is not configured!");
        _channelLink = configuration["TelegramChannel:ChannelLink"]
            ?? throw new ArgumentNullException("Channel Link is not configured!");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var me = await _client.GetMeAsync(cancellationToken);
            Console.WriteLine($"Bot connected as: {me.Username}");

            var offset = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var updates = await _client.GetUpdatesAsync(offset, cancellationToken: cancellationToken);
                    foreach (var update in updates)
                    {
                        await HandleUpdateAsync(update, cancellationToken);
                        offset = update.Id + 1;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error in polling: {ex.Message}");
                    await Task.Delay(1000, cancellationToken);
                }
                await Task.Delay(500, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error starting bot: {ex.Message}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Bot is stopping...");
        return Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.Message && update.Message != null)
        {
            var message = update.Message;
            var chatId = message.Chat.Id;
            var text = message.Text;

            using var scope = _scopeFactory.CreateScope();
            var questionService = scope.ServiceProvider.GetRequiredService<IQuestionService>();
            var optionService = scope.ServiceProvider.GetRequiredService<IOptionService>();
            var responseService = scope.ServiceProvider.GetRequiredService<IResponseService>();
            var subjectService = scope.ServiceProvider.GetRequiredService<ISubjectService>();

            if (_pendingBroadcast.ContainsKey(chatId) && _pendingBroadcast[chatId])
            {
                if (await IsUserAdminAsync(chatId, cancellationToken))
                {
                    await HandleBroadcastMessageAsync(chatId, text, scope.ServiceProvider, cancellationToken);
                    return;
                }
                else
                {
                    _pendingBroadcast.Remove(chatId);
                    await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд паём фиристанд!", cancellationToken: cancellationToken);
                }
            }

            if (message.Document != null)
            {
                if (await IsUserAdminAsync(chatId, cancellationToken))
                {
                    if (!_userCurrentSubject.ContainsKey(chatId))
                    {
                        await _client.SendMessage(chatId,
                            "❌ Лутфан аввал фанро интихоб кунед!",
                            replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                            cancellationToken: cancellationToken);
                        return;
                    }
                    await HandleFileUploadAsync(message, questionService, subjectService, cancellationToken);
                }
                else
                {
                    await _client.SendMessage(chatId,
                        "❌ Танҳо админҳо метавонанд файл боргузорӣ кунанд!",
                        cancellationToken: cancellationToken);
                }
                return;
            }

            if (text != "/start" && text != "/register")
            {
                if (!await CheckChannelSubscriptionAsync(chatId, cancellationToken))
                {
                    return;
                }
            }

            if (message.Contact != null)
            {
                await HandleContactRegistrationAsync(message, scope.ServiceProvider, cancellationToken);
                return;
            }

            if (_pendingRegistrations.ContainsKey(chatId))
            {
                var reg = _pendingRegistrations[chatId];
                if (!reg.IsNameReceived)
                {
                    await HandleNameRegistrationAsync(chatId, text, cancellationToken);
                    return;
                }
                else if (reg.IsNameReceived && !reg.IsCityReceived)
                {
                    await HandleCityRegistrationAsync(chatId, text, scope.ServiceProvider, cancellationToken);
                    return;
                }
            }

            switch (text)
            {
                case "/start":
                    if (!await IsUserRegisteredAsync(chatId, scope.ServiceProvider, cancellationToken))
                    {
                        await SendRegistrationRequestAsync(chatId, cancellationToken);
                    }
                    else
                    {
                        await _client.SendMessage(chatId,
                            "Хуш омадед! Барои оғоз тугмаи 'Саволи нав'-ро пахш кунед.",
                            replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                            cancellationToken: cancellationToken);
                    }
                    break;

                case "/register":
                    if (!await IsUserRegisteredAsync(chatId, scope.ServiceProvider, cancellationToken))
                    {
                        await SendRegistrationRequestAsync(chatId, cancellationToken);
                    }
                    else
                    {
                        await _client.SendMessage(chatId,
                            "Шумо аллакай сабт шудаед!",
                            cancellationToken: cancellationToken);
                    }
                    break;

                case "❓ Саволи нав":
                    if (!await IsUserRegisteredAsync(chatId, scope.ServiceProvider, cancellationToken))
                    {
                        await _client.SendMessage(chatId,
                            "Лутфан аввал ба бот сабт шавед. Барои сабт /register-ро пахш кунед.",
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await HandleNewQuestionAsync(chatId, questionService, subjectService, cancellationToken);
                    }
                    break;

                case "🏆 Топ":
                    await HandleTopCommandAsync(chatId, scope.ServiceProvider, cancellationToken);
                    break;

                case "👤 Профил":
                    await HandleProfileCommandAsync(chatId, scope.ServiceProvider, cancellationToken);
                    break;

                case "ℹ️ Кумак":
                    await HandleHelpCommandAsync(chatId, cancellationToken);
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
                        replyMarkup: subjectKeyboard,
                        cancellationToken: cancellationToken);
                    break;

                case "🧪 Химия":
                case "🔬 Биология":
                case "📖 Забони тоҷикī":
                case "🌍 English":
                case "📜 Таърих":
                    await HandleSubjectSelectionAsync(chatId, text, cancellationToken);
                    break;

                case "⬅️ Бозгашт":
                    await _client.SendMessage(chatId,
                        "Менюи асосӣ",
                        replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                        cancellationToken: cancellationToken);
                    break;

                case "👨‍💼 Админ":
                    await HandleAdminCommandAsync(chatId, cancellationToken);
                    break;

                case "📢 Фиристодани паём":
                    if (await IsUserAdminAsync(chatId, cancellationToken))
                    {
                        _pendingBroadcast[chatId] = true;
                        await _client.SendMessage(chatId,
                            "Лутфан паёми худро барои фиристодан ба ҳамаи корбарон ворид кунед:",
                            replyMarkup: new ReplyKeyboardRemove(),
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await _client.SendMessage(chatId,
                            "❌ Танҳо админҳо метавонанд паём фиристанд!",
                            cancellationToken: cancellationToken);
                    }
                    break;

                case "📊 Омор":
                    if (await IsUserAdminAsync(chatId, cancellationToken))
                    {
                        await HandleStatisticsCommandAsync(chatId, scope.ServiceProvider, cancellationToken);
                    }
                    else
                    {
                        await _client.SendMessage(chatId,
                            "❌ Танҳо админҳо метавонанд оморро бубинанд!",
                            cancellationToken: cancellationToken);
                    }
                    break;

                case "📝 Саволҳо":
                    if (await IsUserAdminAsync(chatId, cancellationToken))
                    {
                        await _client.SendMessage(chatId,
                            "Функсияи 'Саволҳо' ҳанӯз амалӣ нашудааст.",
                            replyMarkup: await GetAdminButtonsAsync(cancellationToken),
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await _client.SendMessage(chatId,
                            "❌ Танҳо админҳо метавонанд саволҳоро бубинанд!",
                            cancellationToken: cancellationToken);
                    }
                    break;

                default:
                    await _client.SendMessage(chatId, "Фармони нодуруст!", cancellationToken: cancellationToken);
                    break;
            }
        }
        else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            using var scope = _scopeFactory.CreateScope();
            var questionService = scope.ServiceProvider.GetRequiredService<IQuestionService>();
            var responseService = scope.ServiceProvider.GetRequiredService<IResponseService>();
            await HandleCallbackQueryAsync(update.CallbackQuery, questionService, responseService, cancellationToken);
        }
    }

    // Registration Methods
    private async Task<bool> IsUserRegisteredAsync(long chatId, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        return await dbContext.Users.AnyAsync(u => u.ChatId == chatId, cancellationToken);
    }

    private async Task SendRegistrationRequestAsync(long chatId, CancellationToken cancellationToken)
    {
        var requestContactButton = new KeyboardButton("Telephone Number") { RequestContact = true };
        var keyboard = new ReplyKeyboardMarkup(new List<KeyboardButton> { requestContactButton })
        {
            ResizeKeyboard = true,
            OneTimeKeyboard = true
        };

        await _client.SendMessage(chatId,
            "Барои сабт кардан, лутфан тугмаи зерро пахш кунед!",
            replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleContactRegistrationAsync(Message message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
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
                replyMarkup: new ReplyKeyboardRemove(),
                cancellationToken: cancellationToken);
        }
        else
        {
            await _client.SendMessage(chatId,
                "Лутфан номи худро ворид кунед, то сабт ба итмом расад.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleNameRegistrationAsync(long chatId, string name, CancellationToken cancellationToken)
    {
        if (!_pendingRegistrations.ContainsKey(chatId))
            return;

        var regInfo = _pendingRegistrations[chatId];
        regInfo.Name = name;
        regInfo.IsNameReceived = true;

        await _client.SendMessage(chatId,
            "Лутфан шаҳри худро ворид кунед.",
            replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: cancellationToken);
    }

    private async Task HandleCityRegistrationAsync(long chatId, string city, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        if (!_pendingRegistrations.ContainsKey(chatId))
            return;

        var regInfo = _pendingRegistrations[chatId];
        regInfo.City = city;
        regInfo.IsCityReceived = true;

        try
        {
            using var scope = _scopeFactory.CreateScope();
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
            await dbContext.SaveChangesAsync(cancellationToken);

            await _client.SendMessage(chatId,
                "Сабти шумо бо муваффақият анҷом ёфт!\nБарои оғоз тугмаи 'Саволи нав'-ро пахш кунед!",
                replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving user registration: {ex.Message}");
            await _client.SendMessage(chatId,
                "Дар сабти маълумот хатое рӯй дод. Лутфан баъдтар кӯшиш намоед.",
                cancellationToken: cancellationToken);
        }
        finally
        {
            _pendingRegistrations.Remove(chatId);
        }
    }

    // Question Methods
    private async Task<IReplyMarkup> GetMainButtonsAsync(long chatId, CancellationToken cancellationToken)
    {
        var isAdmin = await IsUserAdminAsync(chatId, cancellationToken);
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

    private async Task HandleSubjectSelectionAsync(long chatId, string text, CancellationToken cancellationToken)
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

        var isAdmin = await IsUserAdminAsync(chatId, cancellationToken);
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

        await _client.SendMessage(chatId, message, replyMarkup: keyboard, cancellationToken: cancellationToken);
    }

    private async Task HandleNewQuestionAsync(long chatId, IQuestionService questionService, ISubjectService subjectService, CancellationToken cancellationToken)
    {
        if (!_userCurrentSubject.TryGetValue(chatId, out int currentSubject))
        {
            await _client.SendMessage(chatId,
                "❌ Лутфан аввал фанро интихоб кунед!",
                replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                cancellationToken: cancellationToken);
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
                    InlineKeyboardButton.WithCallbackData("️♻️ Аз нав оғоз кардан!", "restart")),
                cancellationToken: cancellationToken);
            return;
        }

        var question = await questionService.GetRandomQuestionBySubject(currentSubject);
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
                replyMarkup: GetButtons(question.QuestionId),
                cancellationToken: cancellationToken);
        }
        else
        {
            await _client.SendMessage(chatId,
                "❌ Дар айни замон саволҳо барои ин фан дастрас нестанд.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, IQuestionService questionService, IResponseService responseService, CancellationToken cancellationToken)
    {
        if (callbackQuery.Message?.Chat == null || callbackQuery.Data == null)
            return;

        var chatId = callbackQuery.Message.Chat.Id;
        var messageId = callbackQuery.Message.MessageId;

        if (callbackQuery.Data == "check_subscription")
        {
            if (await IsUserChannelMemberAsync(chatId, cancellationToken))
            {
                await _client.DeleteMessageAsync(chatId, messageId, cancellationToken);
                await _client.SendMessage(chatId,
                    "✅ Ташаккур барои обуна! Акнун шумо метавонед аз бот истифода баред.",
                    cancellationToken: cancellationToken);
                return;
            }
            else
            {
                await _client.AnswerCallbackQueryAsync(
                    callbackQuery.Id,
                    "❌ Шумо ҳоло ба канал обуна нашудаед!",
                    showAlert: true,
                    cancellationToken: cancellationToken);
                return;
            }
        }

        var callbackData = callbackQuery.Data.Split('_');

        if (callbackQuery.Data == "restart")
        {
            _userScores[chatId] = 0;
            _userQuestions[chatId] = 0;

            await _client.EditMessageTextAsync(chatId, messageId,
                "Барои тест омодаед? Барои оғоз \"Саволи нав\"-ро пахш кунед.",
                cancellationToken: cancellationToken);
            return;
        }

        if (!int.TryParse(callbackData[0], out int questionId))
            return;

        var question = await questionService.GetQuestionById(questionId);
        if (question == null)
        {
            await _client.EditMessageTextAsync(chatId, messageId, "Савол ёфт нашуд.", cancellationToken: cancellationToken);
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
            await _client.EditMessageTextAsync(chatId, messageId, "Офарин! +1 балл", cancellationToken: cancellationToken);

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);
            if (user != null)
            {
                user.Score += 1;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        else
        {
            await _client.EditMessageTextAsync(chatId, messageId,
                $"❌ Афсūс! Ҷавоби шумо нодуруст!\n" +
                $"💡 Ҷавоби дуруст: {correctAnswer} буд.",
                cancellationToken: cancellationToken);
        }

        var userResponse = new UserResponse
        {
            ChatId = chatId,
            QuestionId = questionId,
            SelectedOption = selectedOptionText,
            IsCorrect = isCorrect
        };
        await responseService.SaveUserResponse(userResponse);
    }

    private async Task<bool> IsUserChannelMemberAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var chatMember = await _client.GetChatMemberAsync(_channelId, chatId, cancellationToken);
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

    private async Task<bool> CheckChannelSubscriptionAsync(long chatId, CancellationToken cancellationToken)
    {
        if (!await IsUserChannelMemberAsync(chatId, cancellationToken))
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithUrl("Обуна шудан ба канал", _channelLink)
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("🔄 Тафтиш", "check_subscription")
                }
            });

            await _client.SendMessage(chatId,
                "⚠️ Барои истифодаи бот, лутфан аввал ба канали мо обуна шавед!",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
            return false;
        }
        return true;
    }

    // Top & Profile & Help
    private async Task HandleTopCommandAsync(long chatId, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var topUsers = await dbContext.Users.OrderByDescending(u => u.Score).Take(50).ToListAsync(cancellationToken);

        if (topUsers.Count == 0)
        {
            await _client.SendMessage(chatId, "Лист холī аст!", cancellationToken: cancellationToken);
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
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                <= 10 => "🔹",
                _ => "⚪"
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

        await _client.SendMessage(chatId, messageText, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
    }

    private async Task HandleProfileCommandAsync(long chatId, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);
        if (user != null)
        {
            int level = GetLevel(user.Score);
            string profileText = $"Profile:\n    {user.Name}\n" +
                                 $"Шаҳр: {user.City}\n" +
                                 $"Score: {user.Score}\n" +
                                 $"Левел: {level}";
            await _client.SendMessage(chatId, profileText, cancellationToken: cancellationToken);
        }
        else
        {
            await _client.SendMessage(chatId,
                "Шумо ҳанūз сабт нашудаед. Лутфан барои сабт /register-ро пахш кунед.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleHelpCommandAsync(long chatId, CancellationToken cancellationToken)
    {
        string helpText = "Дастурҳо:\n" +
                          "/start - оғоз ва санҷиши сабт шудан\n" +
                          "/register - сабт кардани ҳисоби корбар\n" +
                          "Саволи нав - барои гирифтани савол\n" +
                          "Top - барои дидани топ 50 корбар\n" +
                          "Profile - барои дидани маълумоти шахсии шумо\n" +
                          "Help - барои дидани ин рӯйхат\n";
        await _client.SendMessage(chatId, helpText, cancellationToken: cancellationToken);
    }

    private int GetLevel(int score)
    {
        if (score <= 150) return 1;
        else if (score <= 300) return 2;
        else if (score <= 450) return 3;
        else if (score <= 600) return 4;
        else return 5;
    }

    // Broadcast Message
    private async Task HandleBroadcastMessageAsync(long chatId, string messageText, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(messageText))
        {
            await _client.SendMessage(chatId,
                "❌ Паём набояд холī бошад! Лутфан паёми дигар ворид кунед.",
                cancellationToken: cancellationToken);
            return;
        }

        _pendingBroadcast.Remove(chatId);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
            var users = await dbContext.Users.Select(u => u.ChatId).ToListAsync(cancellationToken);

            int sentCount = 0;
            foreach (var userChatId in users)
            {
                try
                {
                    await _client.SendMessage(userChatId,
                        $"📢 Паёми муҳим:\n{messageText}",
                        cancellationToken: cancellationToken);
                    sentCount++;
                    await Task.Delay(50, cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error sending message to user {userChatId}: {ex.Message}");
                }
            }

            await _client.SendMessage(chatId,
                $"✅ Паём бо муваффақият ба {sentCount} корбар фиристода шуд!",
                replyMarkup: await GetAdminButtonsAsync(cancellationToken),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error broadcasting message: {ex.Message}");
            await _client.SendMessage(chatId,
                "❌ Хатогī ҳангоми фиристодани паём ба корбарон. Лутфан боз кӯшиш кунед.",
                replyMarkup: await GetAdminButtonsAsync(cancellationToken),
                cancellationToken: cancellationToken);
        }
    }

    // Statistics
    private async Task HandleStatisticsCommandAsync(long chatId, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();

            var activeUsersCount = await dbContext.Users.CountAsync(cancellationToken);
            var questionCounts = await dbContext.Questions
                .GroupBy(q => q.SubjectId)
                .Select(g => new { SubjectId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.SubjectId, g => g.Count, cancellationToken);

            var subjects = await dbContext.Subjects.ToListAsync(cancellationToken);
            var subjectStats = subjects.Select(s =>
                $"{s.Name}: {(questionCounts.TryGetValue(s.Id, out int count) ? count : 0)} савол").ToList();

            var statsMessage = "<b>📊 Омор</b>\n\n" +
                              $"👥 <b>Корбарони фаъол</b>: {activeUsersCount} нафар\n" +
                              $"\n📚 <b>Миқдори саволҳо аз рӯи фанҳо</b>:\n" +
                              string.Join("\n", subjectStats);

            await _client.SendMessage(chatId, statsMessage,
                parseMode: ParseMode.Html,
                replyMarkup: await GetAdminButtonsAsync(cancellationToken),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving statistics: {ex.Message}");
            await _client.SendMessage(chatId,
                "❌ Хатогī ҳангоми гирифтани омор. Лутфан боз кӯшиш кунед.",
                replyMarkup: await GetAdminButtonsAsync(cancellationToken),
                cancellationToken: cancellationToken);
        }
    }

    // File Upload
    private async Task HandleFileUploadAsync(Message message, IQuestionService questionService, ISubjectService subjectService, CancellationToken cancellationToken)
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
                "❌ Лутфан танҳо файли .docx равон кунед!",
                cancellationToken: cancellationToken);
            return;
        }

        try
        {
            var file = await _client.GetFileAsync(message.Document.FileId, cancellationToken);
            if (file.FilePath == null)
            {
                throw new Exception("Could not get file path from Telegram");
            }

            using var stream = new MemoryStream();
            await _client.DownloadFile(file.FilePath, stream, cancellationToken);
            stream.Position = 0;

            if (!_userCurrentSubject.TryGetValue(chatId, out int currentSubject))
            {
                await _client.SendMessage(chatId,
                    "❌ Лутфан аввал фанро интихоб кунед!",
                    cancellationToken: cancellationToken);
                return;
            }

            await NotifyAdminsAsync($"📥 Файли нав аз {username}\nНоми файл: {fileName}\nБа коркард дода шуд...", cancellationToken);

            var questions = ParseQuestionsDocx.ParseQuestionsFromDocx(stream, currentSubject);

            foreach (var question in questions)
            {
                await questionService.CreateQuestion(question);
            }

            var successMessage = $"✅ {questions.Count} савол бо муваффақият илова карда шуд!";
            await _client.SendMessage(chatId, successMessage, cancellationToken: cancellationToken);

            await NotifyAdminsAsync($"✅ Аз файли {fileName}\n" +
                                   $"Ки аз тарафи {username} фиристода шуда буд,\n" +
                                   $"{questions.Count} савол бо муваффақият илова карда шуд!", cancellationToken);
        }
        catch (Exception ex)
        {
            var errorMessage = $"❌ Хатогī: {ex.Message}";
            await _client.SendMessage(chatId, errorMessage, cancellationToken: cancellationToken);

            await NotifyAdminsAsync($"❌ Хатогī ҳангоми коркарди файл:\n" +
                                   $"Файл: {fileName}\n" +
                                   $"Корбар: {username}\n" +
                                   $"Хатогī: {ex.Message}", cancellationToken);
        }
    }

    private async Task NotifyAdminsAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            var chatMembers = await _client.GetChatAdministratorsAsync(_channelId, cancellationToken);

            foreach (var member in chatMembers)
            {
                if (member.Status is ChatMemberStatus.Creator or ChatMemberStatus.Administrator)
                {
                    try
                    {
                        await _client.SendMessage(member.User.Id, message, cancellationToken: cancellationToken);
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

 
    // Admin Panel
    private async Task<bool> IsUserAdminAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var chatMember = await _client.GetChatMemberAsync(_channelId, chatId, cancellationToken);
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

    private async Task<IReplyMarkup> GetAdminButtonsAsync(CancellationToken cancellationToken)
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

    private async Task HandleAdminCommandAsync(long chatId, CancellationToken cancellationToken)
    {
        var isAdmin = await IsUserAdminAsync(chatId, cancellationToken);
        Console.WriteLine($"Admin command requested by ChatId {chatId}: IsAdmin={isAdmin}");

        if (!isAdmin)
        {
            await _client.SendMessage(chatId,
                "❌ Бубахшед, шумо админ нестед! \nБарои админ шудан лутфан ба канал ҳамчун маъмур (админ) ё созанда (криейтор) илова шавед.",
                cancellationToken: cancellationToken);
            return;
        }

        await _client.SendMessage(chatId,
            "Хуш омадед ба панели админ!\n" +
            "Лутфан амалро интихоб кунед:",
            replyMarkup: await GetAdminButtonsAsync(cancellationToken),
            cancellationToken: cancellationToken);
    }
}