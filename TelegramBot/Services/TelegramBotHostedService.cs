using Domain.DTOs;
using Microsoft.EntityFrameworkCore;
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
    private readonly Dictionary<long, (int QuestionId, DateTime StartTime, bool IsAnswered, IReplyMarkup Markup, int MessageId)> _activeQuestions = new();
    private readonly Dictionary<long, CancellationTokenSource> _questionTimers = new();    private const int MaxQuestions = 10;
    private const int QuestionTimeLimit = 30;
    private readonly HashSet<int> NoTimerSubjects = new() { 1, 8, 10 }; // 1 - Химия, 8 - Физика, 10 - Математика

    // Конструктор барои ибтидои бот
    public TelegramBotHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        var token = configuration["BotConfiguration:Token"] ?? throw new ArgumentNullException("Токени Боти Telegram ёфт нашуд!");
        _client = new TelegramBotClient(token);
        _channelId = configuration["TelegramChannel:ChannelId"] ?? throw new ArgumentNullException("ID-и канал ёфт нашуд!");
        _channelLink = configuration["TelegramChannel:ChannelLink"] ?? throw new ArgumentNullException("Пайванди канал ёфт нашуд!");
    }

    // Оғози фаъолияти бот
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var me = await _client.GetMeAsync(cancellationToken);
            Console.WriteLine($"Бот бо номи {me.Username} пайваст шуд");

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
                    Console.WriteLine($"Хатогӣ дар дархост: {ex.Message}");
                    await Task.Delay(1000, cancellationToken);
                }
                await Task.Delay(500, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ ҳангоми оғози бот: {ex.Message}");
        }
    }

    // Қатъ кардани фаъолияти бот
    public Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Бот қатъ карда мешавад...");
        return Task.CompletedTask;
    }

    // Идоракунии навсозиҳо (update) аз Telegram
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
                if (text == "❌ Бекор кардан")
                {
                    CleanupBroadcastState(chatId);
                    await _client.SendMessage(chatId, "Фиристодани паём бекор карда шуд!", replyMarkup: GetAdminButtons(), cancellationToken: cancellationToken);
                    return;
                }
                await HandleBroadcastMessageAsync(chatId, text, scope.ServiceProvider, cancellationToken);
                return;
            }

            if (message.Document != null)
            {
                if (await IsUserAdminAsync(chatId, cancellationToken))
                {
                    if (!_userCurrentSubject.ContainsKey(chatId))
                    {
                        await _client.SendMessage(chatId, "❌ Лутфан, аввал фанро интихоб кунед!", replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken), cancellationToken: cancellationToken);
                        return;
                    }
                    await HandleFileUploadAsync(message, questionService, subjectService, cancellationToken);
                }
                else
                {
                    await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд файл бор кунанд!", cancellationToken: cancellationToken);
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
                        await _client.SendMessage(chatId, "Хуш омаед! Барои оғози тест тугмаи 'Оғози тест'-ро пахш кунед.", replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken), cancellationToken: cancellationToken);
                    }
                    break;

                case "/register":
                    if (!await IsUserRegisteredAsync(chatId, scope.ServiceProvider, cancellationToken))
                    {
                        await SendRegistrationRequestAsync(chatId, cancellationToken);
                    }
                    else
                    {
                        await _client.SendMessage(chatId, "Шумо аллакай сабти ном шудаед!", cancellationToken: cancellationToken);
                    }
                    break;

                case "🎯 Оғози тест":
                    if (!await IsUserRegisteredAsync(chatId, scope.ServiceProvider, cancellationToken))
                    {
                        await _client.SendMessage(chatId, "Лутфан, аввал дар бот сабти ном кунед. Барои сабти ном /register -ро пахш кунед.", cancellationToken: cancellationToken);
                    }
                    else
                    {
                        _userScores[chatId] = 0;
                        _userQuestions[chatId] = 0;
                        await HandleNewQuestionAsync(chatId, questionService, subjectService, cancellationToken);
                    }
                    break;

                case "🏆 Беҳтаринҳо":
                    await HandleTopCommandAsync(chatId, scope.ServiceProvider, cancellationToken);
                    break;

                case "👤 Профил":
                    await HandleProfileCommandAsync(chatId, scope.ServiceProvider, cancellationToken);
                    break;

                case "ℹ️ Кӯмак":
                    await HandleHelpCommandAsync(chatId, cancellationToken);
                    break;

                case "📚 Интихоби фан":                    var subjectKeyboard = new ReplyKeyboardMarkup
                    {
                        Keyboard = new List<List<KeyboardButton>>
                        {
                            new() { new KeyboardButton("🧪 Химия"), new KeyboardButton("🔬 Биология") },
                            new() { new KeyboardButton("📖 Забони тоҷикӣ"), new KeyboardButton("🌍 Забони англисӣ") },
                            new() { new KeyboardButton("📜 Таърих"), new KeyboardButton("🌍 География") },
                            new() { new KeyboardButton("📚 Адабиёти тоҷик"), new KeyboardButton("⚛️ Физика") },
                            new() { new KeyboardButton("🇷🇺 Забони русӣ"), new KeyboardButton("📐 Математика") },
                            new() { new KeyboardButton("⬅️ Бозгашт") }
                        },
                        ResizeKeyboard = true
                    };
                    await _client.SendMessage(chatId, "Лутфан, фанро интихоб кунед:", replyMarkup: subjectKeyboard, cancellationToken: cancellationToken);
                    break;

                case "🧪 Химия":
                case "🔬 Биология":
                case "📖 Забони тоҷикӣ":
                case "🌍 Забони англисӣ":
                case "📜 Таърих":
                case "🌍 География":
                case "📚 Адабиёти тоҷик":
                case "⚛️ Физика":
                case "🇷🇺 Забони русӣ":
                case "📐 Математика":
                    await HandleSubjectSelectionAsync(chatId, text, cancellationToken);
                    break;

                case "⬅️ Бозгашт":
                    await _client.SendMessage(chatId, "Бозгашт ба менюи асосӣ", replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken), cancellationToken: cancellationToken);
                    break;

                case "👨‍💼 Админ":
                    await HandleAdminCommandAsync(chatId, cancellationToken);
                    break;

                case "📢 Фиристодани паём":
                    if (await IsUserAdminAsync(chatId, cancellationToken))
                    {
                        _pendingBroadcast[chatId] = true;
                        var cancelKeyboard = new ReplyKeyboardMarkup(new[] { new KeyboardButton("❌ Бекор кардан") }) { ResizeKeyboard = true };
                        await _client.SendMessage(chatId, "📢 Лутфан, паёмеро, ки ба ҳамаи корбарон фиристода мешавад, ворид кунед:", replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд паём фиристанд!", cancellationToken: cancellationToken);
                    }
                    break;
                case "📊 Омор":
                if (await IsUserAdminAsync(chatId, cancellationToken))
                {
                    await HandleStatisticsCommandAsync(chatId, scope.ServiceProvider, cancellationToken);
                }
                else
                {
                    await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд оморро бубинанд!", cancellationToken: cancellationToken);
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
            var subjectService = scope.ServiceProvider.GetRequiredService<ISubjectService>();
            await HandleCallbackQueryAsync(update.CallbackQuery, questionService, responseService, subjectService, cancellationToken);
        }
    }

    // Санҷиши сабти номи корбар
    private async Task<bool> IsUserRegisteredAsync(long chatId, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        return await dbContext.Users.AnyAsync(u => u.ChatId == chatId, cancellationToken);
    }

    // Дархост барои сабти ном
    private async Task SendRegistrationRequestAsync(long chatId, CancellationToken cancellationToken)
    {
        var requestContactButton = new KeyboardButton("Рақами телефон") { RequestContact = true };
        var keyboard = new ReplyKeyboardMarkup(new[] { new[] { requestContactButton } }) { ResizeKeyboard = true, OneTimeKeyboard = true };
        await _client.SendMessage(chatId, "Барои сабти ном тугмаи зеринро пахш кунед!", replyMarkup: keyboard, cancellationToken: cancellationToken);
    }

    // Идоракунии сабти ном бо рақами телефон
    private async Task HandleContactRegistrationAsync(Message message, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var contact = message.Contact;
        var autoUsername = !string.IsNullOrWhiteSpace(message.Chat.Username) ? message.Chat.Username : message.Chat.FirstName;

        if (!_pendingRegistrations.ContainsKey(chatId))
        {
            _pendingRegistrations[chatId] = new RegistrationInfo { Contact = contact, AutoUsername = autoUsername, IsNameReceived = false, IsCityReceived = false };
            await _client.SendMessage(chatId, "Ташаккур! Акнун номатонро ворид кунед.", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
        }
        else
        {
            await _client.SendMessage(chatId, "Лутфан, номатонро ворид кунед, то сабти номро анҷом диҳед.", cancellationToken: cancellationToken);
        }
    }

    // Идоракунии ворид кардани ном
    private async Task HandleNameRegistrationAsync(long chatId, string name, CancellationToken cancellationToken)
    {
        if (!_pendingRegistrations.ContainsKey(chatId)) return;
        var regInfo = _pendingRegistrations[chatId];
        regInfo.Name = name;
        regInfo.IsNameReceived = true;
        await _client.SendMessage(chatId, "Лутфан, шаҳратонро ворид кунед.", replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
    }

    // Идоракунии ворид кардани шаҳр ва анҷоми сабти ном
    private async Task HandleCityRegistrationAsync(long chatId, string city, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        if (!_pendingRegistrations.ContainsKey(chatId)) return;
        var regInfo = _pendingRegistrations[chatId];
        regInfo.City = city;
        regInfo.IsCityReceived = true;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
            var user = new User { ChatId = chatId, Username = regInfo.AutoUsername, Name = regInfo.Name, PhoneNumber = regInfo.Contact.PhoneNumber, City = regInfo.City, Score = 0 };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);
            await _client.SendMessage(chatId, "Сабти номи шумо бо муваффақият анҷом ёфт!\nБарои оғози тест тугмаи 'Оғози тест'-ро пахш кунед!", replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken), cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ ҳангоми сабти корбар: {ex.Message}");
            await _client.SendMessage(chatId, "Хатогӣ ҳангоми сабти маълумот рух дод. Лутфан, баъдтар дубора кӯшиш кунед.", cancellationToken: cancellationToken);
        }
        finally
        {
            _pendingRegistrations.Remove(chatId);
        }
    }

    // Тугмаҳои асосии меню
    private async Task<IReplyMarkup> GetMainButtonsAsync(long chatId, CancellationToken cancellationToken)
    {
        var isAdmin = await IsUserAdminAsync(chatId, cancellationToken);
        var buttons = new List<List<KeyboardButton>>
        {
            new() { new KeyboardButton("📚 Интихоби фан"), new KeyboardButton("🎯 Оғози тест") },
            new() { new KeyboardButton("🏆 Беҳтаринҳо"), new KeyboardButton("👤 Профил") },
            new() { new KeyboardButton("ℹ️ Кӯмак") }
        };
        if (isAdmin) buttons.Add(new() { new KeyboardButton("👨‍💼 Админ") });
        return new ReplyKeyboardMarkup(buttons) { ResizeKeyboard = true };
    }

    // Тугмаҳои интихоби ҷавобҳо барои саволҳо
    private IReplyMarkup GetButtons(int questionId)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[] { InlineKeyboardButton.WithCallbackData("▫️ A", $"{questionId}_A"), InlineKeyboardButton.WithCallbackData("▫️ B", $"{questionId}_B") },
            new[] { InlineKeyboardButton.WithCallbackData("▫️ C", $"{questionId}_C"), InlineKeyboardButton.WithCallbackData("▫️ D", $"{questionId}_D") }
        });
    }

    // Интихоби фан
    private async Task HandleSubjectSelectionAsync(long chatId, string text, CancellationToken cancellationToken)
    {        int subjectId = text switch
        {
            "🧪 Химия" => 1,
            "🔬 Биология" => 2,
            "📖 Забони тоҷикӣ" => 3,
            "🌍 Забони англисӣ" => 4,
            "📜 Таърих" => 5,
            "🌍 География" => 6,
            "📚 Адабиёти тоҷик" => 7,
            "⚛️ Физика" => 8,
            "🇷🇺 Забони русӣ" => 9,
            "📐 Математика" => 10,
            _ => 0
        };
        if (subjectId == 0) return;
        _userCurrentSubject[chatId] = subjectId;
        var isAdmin = await IsUserAdminAsync(chatId, cancellationToken);
        var buttons = new List<List<KeyboardButton>>();
        string message;
        if (isAdmin)
        {
            buttons.Add(new() { new KeyboardButton("📤 Боркунии файл") });
            message = $"Шумо фани {text}-ро интихоб кардед.\nБарои илова кардани саволҳо файли .docx фиристед.";
        }
        else
        {
            buttons.Add(new() { new KeyboardButton("🎯 Оғози тест") });
            message = $"Шумо фани {text}-ро интихоб кардед.\nБарои оғози тест тугмаи 'Оғози тест'-ро пахш кунед.";
        }
        buttons.Add(new() { new KeyboardButton("⬅️ Бозгашт") });
        var keyboard = new ReplyKeyboardMarkup(buttons) { ResizeKeyboard = true };
        await _client.SendMessage(chatId, message, replyMarkup: keyboard, cancellationToken: cancellationToken);
    }

    // Фиристодани саволи нав
    private async Task HandleNewQuestionAsync(long chatId, IQuestionService questionService, ISubjectService subjectService, CancellationToken cancellationToken)
    {
        if (!_userCurrentSubject.TryGetValue(chatId, out int currentSubject))
        {
            await _client.SendMessage(chatId, "❌ Лутфан, аввал фанро интихоб кунед!", replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken), cancellationToken: cancellationToken);
            return;
        }

        if (_userQuestions[chatId] >= MaxQuestions)
        {
            string resultText = $"<b>📝 Тест ба охир расид!</b>\nХолҳои шумо: {_userScores[chatId]}/{MaxQuestions}.";
            var restartButton = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("♻️ Аз нав оғоз кунед!", "restart"));
            await _client.SendMessage(chatId, resultText, parseMode: ParseMode.Html, replyMarkup: restartButton, cancellationToken: cancellationToken);
            return;
        }

        var question = await questionService.GetRandomQuestionBySubject(currentSubject);
        if (question != null)
        {
            _userQuestions[chatId]++;
            if (_questionTimers.TryGetValue(chatId, out var oldTimer))
            {
                oldTimer.Cancel();
                _questionTimers.Remove(chatId);
            }            var markup = GetButtons(question.QuestionId);
            var messageText = $"<b>📚 Фан: {question.SubjectName}</b>\n\n" +
                $"❓ {question.QuestionText}\n\n" +
                $"A) {question.FirstOption}\n" +
                $"B) {question.SecondOption}\n" +
                $"C) {question.ThirdOption}\n" +
                $"D) {question.FourthOption}";

            // Добавляем таймер только для предметов, которые не в списке NoTimerSubjects
            if (!NoTimerSubjects.Contains(currentSubject))
            {
                messageText += $"\n\n<i>⏱ Вақт: {QuestionTimeLimit} сония</i>";
            }

            var sentMessage = await _client.SendMessage(chatId,
                messageText,
                parseMode: ParseMode.Html, 
                replyMarkup: markup, 
                cancellationToken: cancellationToken);            _activeQuestions[chatId] = (question.QuestionId, DateTime.UtcNow, false, markup, sentMessage.MessageId);
            
            // Запускаем таймер только для предметов, которые не в списке NoTimerSubjects
            if (!NoTimerSubjects.Contains(currentSubject))
            {
                var cts = new CancellationTokenSource();
                _questionTimers[chatId] = cts;
                _ = UpdateQuestionTimer(chatId, cts.Token);
            }
        }
        else
        {
            await _client.SendMessage(chatId, "❌ Дар айни замон саволҳо барои ин фан дастрас нестанд.", cancellationToken: cancellationToken);
        }
    }

    // Навсозии таймери савол
    private async Task UpdateQuestionTimer(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            if (_activeQuestions.TryGetValue(chatId, out var questionInfo) && !questionInfo.IsAnswered)
            {
                using var scope = _scopeFactory.CreateScope();
                var questionService = scope.ServiceProvider.GetRequiredService<IQuestionService>();
                var question = await questionService.GetQuestionById(questionInfo.QuestionId);
                if (question == null) return;

                int remainingTime = QuestionTimeLimit;
                while (remainingTime > 0 && !questionInfo.IsAnswered)
                {
                    await Task.Delay(1000, cancellationToken);
                    remainingTime--;

                    if (_activeQuestions.TryGetValue(chatId, out var updatedInfo) && !updatedInfo.IsAnswered)
                    {
                        await _client.EditMessageText(chatId, updatedInfo.MessageId,
                            $"<b>📚 Фан: {question.SubjectName}</b>\n\n" +
                            $"❓ {question.QuestionText}\n\n" +
                            $"A) {question.FirstOption}\n" +
                            $"B) {question.SecondOption}\n" +
                            $"C) {question.ThirdOption}\n" +
                            $"D) {question.FourthOption}\n\n" +
                            $"<i>⏱ Вақт: {remainingTime} сония</i>",
                            parseMode: ParseMode.Html,
                            replyMarkup: (InlineKeyboardMarkup)updatedInfo.Markup,
                            cancellationToken: cancellationToken);
                    }
                }

                if (_activeQuestions.TryGetValue(chatId, out var finalInfo) && !finalInfo.IsAnswered)
                {
                    var responseService = scope.ServiceProvider.GetRequiredService<IResponseService>();
                    var updatedMarkup = UpdateButtonsMarkup(finalInfo.QuestionId, null, false, question.Answer, question);
                    await _client.EditMessageReplyMarkupAsync(chatId, finalInfo.MessageId, replyMarkup: updatedMarkup, cancellationToken: cancellationToken);

                    var userResponse = new UserResponse 
                    { 
                        ChatId = chatId, 
                        QuestionId = finalInfo.QuestionId, 
                        SelectedOption = "Ҷавоб дода нашуд", 
                        IsCorrect = false 
                    };
                    await responseService.SaveUserResponse(userResponse);
                    _activeQuestions[chatId] = (finalInfo.QuestionId, finalInfo.StartTime, true, finalInfo.Markup, finalInfo.MessageId);

                    if (_userQuestions[chatId] < MaxQuestions)
                    {
                        var subjectService = scope.ServiceProvider.GetRequiredService<ISubjectService>();
                        await HandleNewQuestionAsync(chatId, questionService, subjectService, cancellationToken);
                    }
                    else
                    {
                        string resultText = $"<b>📝 Тест ба охир расид!</b>\nХолҳои шумо: {_userScores[chatId]}/{MaxQuestions}.";
                        var restartButton = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("♻️ Аз нав оғоз кунед!", "restart"));
                        await _client.SendMessage(chatId, resultText, parseMode: ParseMode.Html, replyMarkup: restartButton, cancellationToken: cancellationToken);
                    }
                }
            }
        }
        catch (TaskCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ дар таймер: {ex.Message}");
        }
    }

    // Идоракунии ҷавобҳо ба саволҳо
    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, IQuestionService questionService, IResponseService responseService, ISubjectService subjectService, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message.Chat.Id;
        if (callbackQuery.Data == "restart")
        {
            _userScores[chatId] = 0;
            _userQuestions[chatId] = 0;
            if (_questionTimers.TryGetValue(chatId, out var questionTimer))
            {
                questionTimer.Cancel();
                _questionTimers.Remove(chatId);
            }
            _activeQuestions.Remove(chatId);
            await _client.SendMessage(chatId, "Тест аз нав оғоз шуд!\nБарои идома додан тугмаи 'Оғози тест'-ро пахш кунед.", replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken), cancellationToken: cancellationToken);
            return;
        }

        var callbackData = callbackQuery.Data.Split('_');
        if (!int.TryParse(callbackData[0], out int questionId)) return;
        if (!_activeQuestions.TryGetValue(chatId, out var questionInfo) || questionInfo.IsAnswered)
        {
            await _client.AnswerCallbackQuery(callbackQuery.Id, "⚠️ Вақти ҷавоб додан гузашт!", showAlert: true, cancellationToken: cancellationToken);
            return;
        }

        var question = await questionService.GetQuestionById(questionId);
        if (question == null)
        {
            await _client.SendMessage(chatId, "Савол ёфт нашуд.", cancellationToken: cancellationToken);
            return;
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

        // Навсозӣ кардани ҳолати савол ба "ҷавоб дода шуд"
        _activeQuestions[chatId] = (questionId, questionInfo.StartTime, true, questionInfo.Markup, questionInfo.MessageId);

        // Қатъ кардани таймер
        if (_questionTimers.TryGetValue(chatId, out var currentTimer))
        {
            currentTimer.Cancel();
            _questionTimers.Remove(chatId);
        }

        // Тағйир додани тугмаҳо барои нишон додани ҷавоби дуруст ва нодуруст
        var updatedMarkup = UpdateButtonsMarkup(questionId, selectedOption, isCorrect, correctAnswer, question);
        await _client.EditMessageReplyMarkupAsync(chatId, questionInfo.MessageId, replyMarkup: updatedMarkup, cancellationToken: cancellationToken);

        // Сабт кардани холҳо агар ҷавоб дуруст бошад
        if (isCorrect)
        {
            _userScores[chatId]++;
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);
            if (user != null)
            {
                user.Score += 1;
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }

        // Сабт кардани ҷавоби корбар
        var userResponse = new UserResponse { ChatId = chatId, QuestionId = questionId, SelectedOption = selectedOptionText, IsCorrect = isCorrect };
        await responseService.SaveUserResponse(userResponse);

        // Фиристодани саволи нав агар тест идома дошта бошад
        if (_userQuestions[chatId] < MaxQuestions)
        {
            await HandleNewQuestionAsync(chatId, questionService, subjectService, cancellationToken);
        }
        else
        {
            string resultText = $"<b>📝 Тест ба охир расид!</b>\nХолҳои шумо: {_userScores[chatId]}/{MaxQuestions}.";
            var restartButton = new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("♻️ Аз нав оғоз кунед!", "restart"));
            await _client.SendMessage(chatId, resultText, parseMode: ParseMode.Html, replyMarkup: restartButton, cancellationToken: cancellationToken);
        }
    }

    // Функсияи ёрирасон барои тағйир додани тугмахои inline
    private InlineKeyboardMarkup UpdateButtonsMarkup(int questionId, string selectedOption, bool isCorrect, string correctAnswer, GetQuestionWithOptionsDTO question)
    {
        var buttons = new List<InlineKeyboardButton[]>();

        // Муайян кардани ҷавоби дуруст (A, B, C, D)
        string correctOption = question.FirstOption.Trim() == correctAnswer ? "A" :
                              question.SecondOption.Trim() == correctAnswer ? "B" :
                              question.ThirdOption.Trim() == correctAnswer ? "C" : "D";

        // Сохтани тугмаҳо бо нишонаҳои мувофиқ
        var row1 = new List<InlineKeyboardButton>();
        var row2 = new List<InlineKeyboardButton>();

        // Тугмаи A
        if (selectedOption == "A")
        {
            row1.Add(InlineKeyboardButton.WithCallbackData($"{(isCorrect ? "✅" : "❌")} A", "dummy"));
        }
        else if (correctOption == "A")
        {
            row1.Add(InlineKeyboardButton.WithCallbackData("✅ A", "dummy"));
        }
        else
        {
            row1.Add(InlineKeyboardButton.WithCallbackData("▫️ A", "dummy"));
        }

        // Тугмаи B
        if (selectedOption == "B")
        {
            row1.Add(InlineKeyboardButton.WithCallbackData($"{(isCorrect ? "✅" : "❌")} B", "dummy"));
        }
        else if (correctOption == "B")
        {
            row1.Add(InlineKeyboardButton.WithCallbackData("✅ B", "dummy"));
        }
        else
        {
            row1.Add(InlineKeyboardButton.WithCallbackData("▫️ B", "dummy"));
        }

        // Тугмаи C
        if (selectedOption == "C")
        {
            row2.Add(InlineKeyboardButton.WithCallbackData($"{(isCorrect ? "✅" : "❌")} C", "dummy"));
        }
        else if (correctOption == "C")
        {
            row2.Add(InlineKeyboardButton.WithCallbackData("✅ C", "dummy"));
        }
        else
        {
            row2.Add(InlineKeyboardButton.WithCallbackData("▫️ C", "dummy"));
        }

        // Тугмаи D
        if (selectedOption == "D")
        {
            row2.Add(InlineKeyboardButton.WithCallbackData($"{(isCorrect ? "✅" : "❌")} D", "dummy"));
        }
        else if (correctOption == "D")
        {
            row2.Add(InlineKeyboardButton.WithCallbackData("✅ D", "dummy"));
        }
        else
        {
            row2.Add(InlineKeyboardButton.WithCallbackData("▫️ D", "dummy"));
        }

        buttons.Add(row1.ToArray());
        buttons.Add(row2.ToArray());

        return new InlineKeyboardMarkup(buttons);
    }

    // Санҷиши аъзогии корбар дар канал
    private async Task<bool> IsUserChannelMemberAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var chatMember = await _client.GetChatMemberAsync(_channelId, chatId, cancellationToken);
            return chatMember.Status is ChatMemberStatus.Member or ChatMemberStatus.Administrator or ChatMemberStatus.Creator;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ ҳангоми санҷиши аъзогии канал: {ex.Message}");
            return false;
        }
    }

    // Санҷиши обунаи корбар ба канал
    private async Task<bool> CheckChannelSubscriptionAsync(long chatId, CancellationToken cancellationToken)
    {
        if (!await IsUserChannelMemberAsync(chatId, cancellationToken))
        {
            var keyboard = new InlineKeyboardMarkup(new[] { new[] { InlineKeyboardButton.WithUrl("Обуна шудан ба канал", _channelLink) }, new[] { InlineKeyboardButton.WithCallbackData("🔄 Санҷиш", "check_subscription") } });
            await _client.SendMessage(chatId, "⚠️ Барои истифодаи бот, аввал ба канали мо обуна шавед!", replyMarkup: keyboard, cancellationToken: cancellationToken);
            return false;
        }
        return true;
    }

    // Намоиши рӯйхати 50 корбари беҳтарин
    private async Task HandleTopCommandAsync(long chatId, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var topUsers = await dbContext.Users.OrderByDescending(u => u.Score).Take(50).ToListAsync(cancellationToken);
        if (topUsers.Count == 0)
        {
            await _client.SendMessage(chatId, "Рӯйхат холист!", cancellationToken: cancellationToken);
            return;
        }
        string GetLevelStars(int level) => new string('⭐', level);
        string GetRankColor(int rank) => rank switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", <= 10 => "🔹", _ => "⚪" };
        int cnt = 0;
        var messageText = "<b>🏆 50 Беҳтарин</b>\n\n<pre>#        Ном ва насаб         Хол  </pre>\n<pre>----------------------------------</pre>\n";
        foreach (var user in topUsers)
        {
            cnt++;
            if (user.Name.Length > 15) user.Name = user.Name[..15] + "...";
            string levelStars = GetLevelStars(GetLevel(user.Score));
            string rankSymbol = GetRankColor(cnt);
            messageText += $"<pre>{cnt,0}.{rankSymbol} {user.Name,-20} |{user.Score,-0}|{rankSymbol,2}</pre>\n";
        }
        await _client.SendMessage(chatId, messageText, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
    }

    // Намоиши профили корбар
    private async Task HandleProfileCommandAsync(long chatId, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);
        if (user != null)
        {
            int level = GetLevel(user.Score);
            string profileText = $"<b>Профил:</b>\n    {user.Name}\n<b>Шаҳр:</b> {user.City}\n<b>Хол:</b> {user.Score}\n<b>Сатҳ:</b> {level}";
            await _client.SendMessage(chatId, profileText, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
        }
        else
        {
            await _client.SendMessage(chatId, "Шумо ҳанӯз сабти ном нашудаед. Барои сабти ном /register -ро пахш кунед.", cancellationToken: cancellationToken);
        }
    }

    // Намоиши роҳнамо
    private async Task HandleHelpCommandAsync(long chatId, CancellationToken cancellationToken)
    {
        string helpText = "<b>Роҳнамо:</b>\n/start - оғоз ва санҷиши сабти ном\n/register - сабти номи ҳисоби корбар\nОғози тест - барои оғози тест\nБеҳтаринҳо - дидани 50 корбари беҳтарин\nПрофил - дидани маълумоти шахсии шумо\nКӯмак - дидани ин рӯйхат\n";
        await _client.SendMessage(chatId, helpText, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
    }

    // Ҳисоби сатҳи корбар
    private int GetLevel(int score) => score switch { <= 150 => 1, <= 300 => 2, <= 450 => 3, <= 600 => 4, _ => 5 };

    // Тоза кардани ҳолати паёмҳои оммавӣ
    private void CleanupBroadcastState(long chatId)
    {
        _pendingBroadcast.Remove(chatId);
    }

    // Идоракунии фиристодани паёмҳои оммавӣ
    private async Task HandleBroadcastMessageAsync(long chatId, string messageText, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        try
        {
            if (!await IsUserAdminAsync(chatId, cancellationToken))
            {
                CleanupBroadcastState(chatId);
                await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд паём фиристанд!", replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken), cancellationToken: cancellationToken);
                return;
            }
            if (string.IsNullOrWhiteSpace(messageText))
            {
                await _client.SendMessage(chatId, "❌ Паём наметавонад холӣ бошад! Лутфан, паёми дигар ворид кунед.", cancellationToken: cancellationToken);
                return;
            }
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
            var users = await dbContext.Users.Select(u => u.ChatId).ToListAsync(cancellationToken);
            if (users.Count == 0)
            {
                CleanupBroadcastState(chatId);
                await _client.SendMessage(chatId, "❌ Дар ҳоли ҳозир ягон корбар барои фиристодани паём нест.", replyMarkup: GetAdminButtons(), cancellationToken: cancellationToken);
                return;
            }
            var statusMessage = await _client.SendMessage(chatId, $"<b>📤 Фиристодани паём оғоз шуд...</b>\n0/{users.Count} корбарон", parseMode: ParseMode.Html, replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            var successCount = 0;
            var failedCount = 0;
            var lastUpdateTime = DateTime.UtcNow;
            var batchSize = 30;
            for (var i = 0; i < users.Count; i += batchSize)
            {
                var batch = users.Skip(i).Take(batchSize);
                foreach (var userId in batch)
                {
                    try
                    {
                        await _client.SendMessage(userId, $"<b>📢 Паёми муҳим:</b>\n\n{messageText}", parseMode: ParseMode.Html, cancellationToken: cancellationToken);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Хатогӣ дар фиристодан ба корбар {userId}: {ex.Message}");
                        failedCount++;
                    }
                    if ((DateTime.UtcNow - lastUpdateTime).TotalSeconds >= 3 || (i + 1) % 100 == 0)
                    {
                        var progress = (double)(successCount + failedCount) / users.Count * 100;
                        var progressBar = MakeProgressBar(progress);
                        await _client.EditMessageText(chatId, statusMessage.MessageId, $"<b>📤 Фиристодани паём идома дорад...</b>\n{progressBar}\n✅ Бо муваффақият: {successCount}\n❌ Ноком: {failedCount}\n📊 Пешрафт: {progress:F1}%", parseMode: ParseMode.Html, cancellationToken: cancellationToken);
                        lastUpdateTime = DateTime.UtcNow;
                    }
                }
                await Task.Delay(500, cancellationToken);
            }
            var resultMessage = $"<b>📬 Фиристодани паём ба иттом расид!</b>\n\n✅ Бо муваффақият фиристода шуд: {successCount}\n❌ Ноком: {failedCount}\n📊 Фоизи муваффақият: {((double)successCount / users.Count * 100):F1}%";
            await _client.SendMessage(chatId, resultMessage, parseMode: ParseMode.Html, replyMarkup: GetAdminButtons(), cancellationToken: cancellationToken);
            await NotifyAdminsAsync($"<b>📢 Натиҷаи фиристодани паёми оммавӣ:</b>\n\n{resultMessage}\n\n🕒 Вақт: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC", cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ дар идоракунии паём: {ex}");
            await _client.SendMessage(chatId, "❌ Хатогӣ ҳангоми коркарди паём. Лутфан боз кӯшиш кунед.", replyMarkup: GetAdminButtons(), cancellationToken: cancellationToken);
        }
        finally
        {
            CleanupBroadcastState(chatId);
        }
    }

    // Statistics
    private async Task HandleStatisticsCommandAsync(long chatId, IServiceProvider serviceProvider, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();

            // Get all user stats
            var totalUsers = await dbContext.Users.CountAsync(cancellationToken);
            var activeUsers = await dbContext.UserResponses
                .Where(r => r.CreatedAt >= DateTime.UtcNow.AddDays(-7))
                .Select(r => r.ChatId)
                .Distinct()
                .CountAsync(cancellationToken);

            // Get questions per subject
            var subjects = await dbContext.Subjects.ToListAsync(cancellationToken);
            var questionCounts = await dbContext.Questions
                .GroupBy(q => q.SubjectId)
                .Select(g => new { SubjectId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.SubjectId, g => g.Count, cancellationToken);

            // Calculate total questions
            var totalQuestions = await dbContext.Questions.CountAsync(cancellationToken);

            // Format subject stats ordered by question count
            var subjectStats = subjects
                .OrderByDescending(s => questionCounts.GetValueOrDefault(s.Id, 0))
                .Select(s => $"• {s.Name}: {(questionCounts.TryGetValue(s.Id, out int count) ? count : 0)} савол")
                .ToList();

            // Build nicely formatted message
            var statsMessage = 
                "<b>📊 ОМОРИ БОТ</b>\n" +
                "<code>━━━━━━━━━━━━━━━━━━━━━━</code>\n\n" +
                "<b>👥 Корбарон:</b>\n" +
                $"• Ҳамагӣ: {totalUsers:N0} нафар\n" +
                $"• Фаъол (7 рӯзи охир): {activeUsers:N0} нафар\n" +
                "<code>━━━━━━━━━━━━━━━━━━━━━━</code>\n\n" +
                "<b>📚 Савол ва тестҳо:</b>\n" +
                $"• Ҳамагӣ саволҳо: {totalQuestions:N0}\n" +
                "<code>━━━━━━━━━━━━━━━━━━━━━━</code>\n\n" +
                "<b>📝 Саволҳо аз рӯи фанҳо:</b>\n" +
                $"{string.Join("\n", subjectStats)}";

            // Send formatted stats
            await _client.SendMessage(
                chatId,
                statsMessage,
                parseMode: ParseMode.Html,
                replyMarkup: GetAdminButtons(),
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ дар гирифтани омор: {ex.Message}");
            await _client.SendMessage(chatId,
                "❌ Хатогӣ ҳангоми гирифтани омор. Лутфан, баъдтар боз кӯшиш кунед.",
                replyMarkup: GetAdminButtons(),
                cancellationToken: cancellationToken);
        }
    }

    // Боркунии файл бо саволҳо
    private async Task HandleFileUploadAsync(Message message, IQuestionService questionService, ISubjectService subjectService, CancellationToken cancellationToken)
    {
        if (message.Document == null) return;
        var chatId = message.Chat.Id;
        var fileName = message.Document.FileName ?? "бе ном.docx";
        var username = !string.IsNullOrWhiteSpace(message.From?.Username) ? $"@{message.From.Username}" : message.From?.FirstName ?? "Корбари номаълум";
        if (!fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            await _client.SendMessage(chatId, "❌ Лутфан, танҳо файли .docx фиристед!", cancellationToken: cancellationToken);
            return;
        }
        try
        {
            var file = await _client.GetFileAsync(message.Document.FileId, cancellationToken);
            if (file.FilePath == null) throw new Exception("Гирифтани роҳи файл аз Telegram ғайримумкин аст");
            using var stream = new MemoryStream();
            await _client.DownloadFile(file.FilePath, stream, cancellationToken);
            stream.Position = 0;
            if (!_userCurrentSubject.TryGetValue(chatId, out int currentSubject))
            {
                await _client.SendMessage(chatId, "❌ Лутфан, аввал фанро интихоб кунед!", cancellationToken: cancellationToken);
                return;
            }
            await NotifyAdminsAsync($"<b>📥 Файли нав аз {username}</b>\nНоми файл: {fileName}\nДар ҳоли коркард...", cancellationToken);
            var questions = ParseQuestionsDocx.ParseQuestionsFromDocx(stream, currentSubject);
            foreach (var question in questions) await questionService.CreateQuestion(question);
            var successMessage = $"<b>✅ {questions.Count} савол бо муваффақият илова шуд!</b>";
            await _client.SendMessage(chatId, successMessage, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
            await NotifyAdminsAsync($"<b>✅ Аз файли {fileName}</b>\nАз ҷониби {username} фиристода шуд,\n{questions.Count} савол бо муваффақият илова шуд!", cancellationToken);
        }
        catch (Exception ex)
        {
            var errorMessage = $"<b>❌ Хатогӣ:</b> {ex.Message}";
            await _client.SendMessage(chatId, errorMessage, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
            await NotifyAdminsAsync($"<b>❌ Хатогӣ ҳангоми коркарди файл:</b>\nФайл: {fileName}\nКорбар: {username}\nХатогӣ: {ex.Message}", cancellationToken);
        }
    }

    // Огоҳ кардани админҳо
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
                        await _client.SendMessage(member.User.Id, message, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
                    }
                    catch (Exception) { }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ ҳангоми огоҳ кардани админҳо: {ex.Message}");
        }
    }

    // Санҷиши вазъи админ
    private async Task<bool> IsUserAdminAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var chatMember = await _client.GetChatMemberAsync(_channelId, chatId, cancellationToken);
            return chatMember.Status is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ ҳангоми санҷиши вазъи админ: {ex.Message}");
            return false;
        }
    }

    // Тугмаҳои панели админ
    private IReplyMarkup GetAdminButtons()
    {
        return new ReplyKeyboardMarkup(new List<List<KeyboardButton>> 
        { 
            new() { new KeyboardButton("📚 Интихоби фан") }, 
            new() { new KeyboardButton("📊 Омор"), new KeyboardButton("📝 Саволҳо") }, 
            new() { new KeyboardButton("📢 Фиристодани паём") }, 
            new() { new KeyboardButton("⬅️ Бозгашт") } 
        }) { ResizeKeyboard = true };
    }

    // Идоракунии панели админ
    private async Task HandleAdminCommandAsync(long chatId, CancellationToken cancellationToken)
    {
        var isAdmin = await IsUserAdminAsync(chatId, cancellationToken);
        if (!isAdmin)
        {
            await _client.SendMessage(chatId, "❌ Бубахшед, шумо админ нестед!\nБарои админ шудан, ба канал ҳамчун маъмур ё созанда илова шавед.", cancellationToken: cancellationToken);
            return;
        }
        await _client.SendMessage(chatId, "Хуш омаед ба панели админ!\nЛутфан, амалро интихоб кунед:", replyMarkup: GetAdminButtons(), cancellationToken: cancellationToken);
    }

    // Сохтани навори пешрафт
    private string MakeProgressBar(double percent)
    {
        var filledCount = (int)(percent / 10);
        var emptyCount = 10 - filledCount;
        return $"[{new string('█', filledCount)}{new string('░', emptyCount)}]";
    }
}