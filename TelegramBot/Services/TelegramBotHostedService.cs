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
using TelegramBot.Services.BookService;


namespace TelegramBot.Services;

public enum UserCheckResult
{
    Success,
    NotMember,
    InvalidUserOrBlocked,
    OtherError
}

public class TelegramBotHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly TelegramBotClient _client;
    private readonly string _channelId;
    private readonly string _channelLink;
    private const string _botUsername = "darsnet_bot";
    private readonly Dictionary<long, RegistrationInfo> _pendingRegistrations = new();
    private readonly Dictionary<long, int> _userScores = new();
    private readonly Dictionary<long, int> _userQuestions = new();
    private readonly Dictionary<long, bool> _pendingBroadcast = new();
    private readonly Dictionary<long, int> _userCurrentSubject = new();

// Track the current step in book upload process
    private readonly Dictionary<long, int> _bookUploadStep = new();

    private readonly
        Dictionary<long, (int QuestionId, DateTime StartTime, bool IsAnswered, IReplyMarkup Markup, int MessageId)>
        _activeQuestions = new();

    private readonly Dictionary<long, CancellationTokenSource> _questionTimers = new();
    private readonly Dictionary<long, DuelGame> _activeGames = new();
    private const int MaxQuestions = 10;
    private const int QuestionTimeLimit = 30;
    private const int MaxDuelRounds = 10;
    private const int BaseScore = 10;
    private const int SpeedBonus = 2;
    private readonly HashSet<int> NoTimerSubjects = new() { 1, 8, 10 }; // 1 - Химия, 8 - Физика, 10 - Математика
    private readonly Dictionary<long, bool> _pendingNameChange = new(); // Track users changing name
    private readonly Dictionary<long, string> _pendingBookNames = new();
    private IBookService _bookService;
    private readonly string _filesDirectory;

    // Pending book info for multi-step admin book upload
    private class PendingBookInfo
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public int? Year { get; set; }
    }

    private readonly Dictionary<long, PendingBookInfo> _pendingBookInfos = new();

    public TelegramBotHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        var token = "8005745055:AAFcsd9NErg8lEz8Zo9XKCxWJXb98PEy9tc"; // Tokeni haqiqii худро иваз кунед
        _client = new TelegramBotClient(token);
        _channelId = configuration["TelegramChannel:ChannelId"] ??
                     throw new ArgumentNullException("ID-и канал ёфт нашуд!");
        _channelLink = configuration["TelegramChannel:ChannelLink"] ??
                       throw new ArgumentNullException("Пайванди канал ёфт нашуд!");
        _filesDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Files", "Books");

        if (!Directory.Exists(_filesDirectory))
        {
            Directory.CreateDirectory(_filesDirectory);
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var me = await _client.GetMe(cancellationToken);
            Console.WriteLine($"Бот бо номи {me.Username} пайваст шуд");

            // Clean up invalid users before starting
            await CleanupInvalidUsersAsync(cancellationToken);

            var offset = 0;
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var updates = await _client.GetUpdates(offset, cancellationToken: cancellationToken);
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

    private async Task CleanupInvalidUsersAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();

            var users = await dbContext.Users.ToListAsync(cancellationToken);
            var invalidUsers = new List<User>();

            foreach (var user in users)
            {
                try
                {
                    // Try to get chat member to check if user is valid
                    await _client.GetChatMember(_channelId, user.ChatId, cancellationToken);
                }
                catch (Exception)
                {
                    // If we get an error, the user is invalid
                    invalidUsers.Add(user);
                    Console.WriteLine($"Корбари нодуруст ёфт шуд: {user.ChatId} - {user.Name}");
                }
            }

            if (invalidUsers.Any())
            {
                dbContext.Users.RemoveRange(invalidUsers);
                await dbContext.SaveChangesAsync(cancellationToken);
                Console.WriteLine($"✅ {invalidUsers.Count} корбари нодуруст аз пойгоҳи додаҳо нест карда шуд");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ ҳангоми тозакунии корбарони нодуруст: {ex.Message}");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Бот қатъ карда мешавад...");
        return Task.CompletedTask;
    }

    private async Task HandleUpdateAsync(Update update, CancellationToken cancellationToken)
    {
        if (update.Type == UpdateType.Message && update.Message != null)
        {
            var message = update.Message;
            var chatId = message.Chat.Id;
            try
            {
                await _client.SendChatAction(chatId, ChatAction.Typing, cancellationToken: cancellationToken);
                var text = message.Text;
                Console.WriteLine(
                    $"[DEBUG] Message from user {chatId}: Text='{text}', Contact={message.Contact != null}, ContactPhone={message.Contact?.PhoneNumber}");

                using var scope = _scopeFactory.CreateScope();
                var questionService = scope.ServiceProvider.GetRequiredService<IQuestionService>();
                var optionService = scope.ServiceProvider.GetRequiredService<IOptionService>();
                var responseService = scope.ServiceProvider.GetRequiredService<IResponseService>();
                var subjectService = scope.ServiceProvider.GetRequiredService<ISubjectService>();

                // Check if user is already registered
                bool isRegistered = await IsUserRegisteredAsync(chatId, scope.ServiceProvider, cancellationToken);

                // Handle pending book input
                if (_pendingBookNames.ContainsKey(chatId))
                {
                    if (message.Document != null)
                    {
                        if (_bookUploadStep.ContainsKey(chatId) && _bookUploadStep[chatId] == 3 &&
                            _pendingBookInfos.ContainsKey(chatId))
                        {
                            await HandleBookUploadAsync(message, _pendingBookInfos[chatId], cancellationToken);
                            _pendingBookInfos.Remove(chatId);
                            _pendingBookNames.Remove(chatId);
                            _bookUploadStep.Remove(chatId);
                            return;
                        }
                        else
                        {
                            await _client.SendMessage(chatId, "❌ Лутфан, аввал маълумоти китобро пурра ворид кунед!",
                                cancellationToken: cancellationToken);
                            return;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        await _client.SendMessage(chatId, "❌ Маълумот наметавонад холӣ бошад!",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    if (!_bookUploadStep.ContainsKey(chatId))
                    {
                        _bookUploadStep[chatId] = 0; // Initialize step
                    }

                    switch (_bookUploadStep[chatId])
                    {
                        case 0: // Waiting for title
                            _pendingBookInfos[chatId] = new PendingBookInfo
                                { Title = text, Description = "", Year = null };
                            _pendingBookNames[chatId] = text;
                            _bookUploadStep[chatId] = 1;
                            await _client.SendMessage(chatId, "📝 Лутфан, тавсифи китобро ворид кунед:",
                                cancellationToken: cancellationToken);
                            break;

                        case 1: // Waiting for description
                            _pendingBookInfos[chatId].Description = text;
                            _bookUploadStep[chatId] = 2;
                            await _client.SendMessage(chatId, "📅 Лутфан, соли нашри китобро ворид кунед:",
                                cancellationToken: cancellationToken);
                            break;

                        case 2: // Waiting for year
                            if (int.TryParse(text, out int year))
                            {
                                _pendingBookInfos[chatId].Year = year;
                                _bookUploadStep[chatId] = 3;
                                await _client.SendMessage(chatId, "✅ Акнун файли китобро фиристед.",
                                    cancellationToken: cancellationToken);
                            }
                            else
                            {
                                await _client.SendMessage(chatId, "❌ Соли нашр бояд рақам бошад!",
                                    cancellationToken: cancellationToken);
                            }

                            break;
                    }

                    return;
                }

                // Handle contact sharing (phone number)
                if (message.Contact != null)
                {
                    if (!isRegistered)
                    {
                        await HandleContactRegistrationAsync(message, scope.ServiceProvider, cancellationToken);
                        return;
                    }
                    else
                    {
                        await _client.SendMessage(chatId, "Шумо аллакай ба қайд гирифта шудаед.",
                            replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                            cancellationToken: cancellationToken);
                        return;
                    }
                }

                // Handle pending registration steps
                if (!isRegistered && _pendingRegistrations.ContainsKey(chatId))
                {
                    var reg = _pendingRegistrations[chatId];

                    if (!reg.IsNameReceived)
                    {
                        Console.WriteLine($"[REGISTRATION] User {chatId} sending name: {text}");
                        await HandleNameRegistrationAsync(chatId, text, cancellationToken);
                        return;
                    }
                    else if (reg.IsNameReceived && !reg.IsCityReceived)
                    {
                        Console.WriteLine($"[REGISTRATION] User {chatId} sending city: {text}");
                        await HandleCityRegistrationAsync(chatId, text, scope.ServiceProvider, cancellationToken);
                        return;
                    }
                }

                // Handle /start command with parameters
                if (text?.StartsWith("/start ") == true)
                {
                    var parameter = text.Substring(7);
                    if (parameter.StartsWith("duel_"))
                    {
                        var parts = parameter.Split('_');
                        if (parts.Length == 3 && long.TryParse(parts[1], out var inviterChatId) &&
                            int.TryParse(parts[2], out var subjectId))
                        {
                            if (chatId == inviterChatId)
                            {
                                await _client.SendMessage(chatId, "❌ Шумо наметавонед худатонро даъват кунед!",
                                    cancellationToken: cancellationToken);
                                return;
                            }

                            if (!isRegistered)
                            {
                                await SendRegistrationRequestAsync(chatId, cancellationToken);
                                return;
                            }

                            await HandleDuelInviteAsync(chatId, inviterChatId, subjectId, cancellationToken);
                            return;
                        }
                    }
                    else if (parameter.StartsWith("ref_"))
                    {
                        var referrerChatId = long.Parse(parameter.Substring(4));
                        if (chatId != referrerChatId)
                        {
                            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
                            var invitation = new Invitation
                            {
                                InviterChatId = referrerChatId,
                                InviteeChatId = chatId,
                                Status = "pending"
                            };
                            dbContext.Invitations.Add(invitation);
                            await dbContext.SaveChangesAsync(cancellationToken);
                        }

                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }
                    }
                }

                // Handle broadcast messages for admins
                if (_pendingBroadcast.ContainsKey(chatId) && _pendingBroadcast[chatId])
                {
                    if (text == "❌ Бекор кардан")
                    {
                        CleanupBroadcastState(chatId);
                        await _client.SendMessage(chatId, "Фиристодани паём бекор карда шуд!",
                            replyMarkup: GetAdminButtons(),
                            cancellationToken: cancellationToken);
                        return;
                    }

                    await HandleBroadcastMessageAsync(chatId, text, scope.ServiceProvider, cancellationToken);
                    return;
                }

                // Handle file uploads for admins
                if (message.Document != null)
                {
                    if (await IsUserAdminAsync(chatId, cancellationToken))
                    {
                        if (!_userCurrentSubject.ContainsKey(chatId))
                        {
                            await _client.SendMessage(chatId, "❌ Лутфан, аввал фанро интихоб кунед!",
                                replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                                cancellationToken: cancellationToken);
                            return;
                        }

                        await HandleFileUploadAsync(message, questionService, subjectService, cancellationToken);
                    }
                    else
                    {
                        await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд файл бор кунанд!",
                            cancellationToken: cancellationToken);
                    }

                    return;
                }

                // Check channel subscription for registered users (except for registration commands)
                if (isRegistered && text != "/start" && text != "/register")
                {
                    if (!await CheckChannelSubscriptionAsync(chatId, cancellationToken))
                    {
                        return;
                    }
                }

                // Handle name change
                if (_pendingNameChange.TryGetValue(chatId, out var pending) && pending)
                {
                    using var innerScope = _scopeFactory.CreateScope();
                    var dbContext = innerScope.ServiceProvider.GetRequiredService<DataContext>();
                    var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);
                    if (user != null && !user.HasChangedName)
                    {
                        user.Name = text;
                        user.HasChangedName = true;
                        await dbContext.SaveChangesAsync(cancellationToken);
                        await _client.SendMessage(chatId, $"Номи шумо ба '{text}' иваз шуд!",
                            replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                            cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await _client.SendMessage(chatId, "Шумо аллакай як бор номи худро иваз кардаед.",
                            replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                            cancellationToken: cancellationToken);
                    }

                    _pendingNameChange.Remove(chatId);
                    return;
                }

                // Handle commands based on registration status
                switch (text)
                {
                    case "/start":
                        Console.WriteLine($"[REGISTRATION] /start command from user {chatId}");
                        if (!isRegistered)
                        {
                            Console.WriteLine(
                                $"[REGISTRATION] User {chatId} is not registered, sending registration request");
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                        }
                        else
                        {
                            Console.WriteLine($"[REGISTRATION] User {chatId} is already registered, showing main menu");
                            await _client.SendMessage(chatId,
                                "Хуш омадед! Барои оғози тест тугмаи 'Оғози тест'-ро пахш кунед.",
                                replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                                cancellationToken: cancellationToken);
                        }

                        break;

                    case "/register":
                        Console.WriteLine($"[REGISTRATION] /register command from user {chatId}");
                        if (!isRegistered)
                        {
                            Console.WriteLine(
                                $"[REGISTRATION] User {chatId} is not registered, sending registration request");
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                        }
                        else
                        {
                            Console.WriteLine($"[REGISTRATION] User {chatId} is already registered, showing message");
                            await _client.SendMessage(chatId, "Шумо аллакай ба қайд гирифта шудаед.",
                                replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                                cancellationToken: cancellationToken);
                        }

                        break;

                    case "Рақами телефон":
                        Console.WriteLine($"[REGISTRATION] User {chatId} pressed 'Рақами телефон' button as text");
                        if (!isRegistered)
                        {
                            Console.WriteLine(
                                $"[REGISTRATION] User {chatId} is not registered, sending registration request again");
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                        }
                        else
                        {
                            await _client.SendMessage(chatId, "Шумо аллакай ба қайд гирифта шудаед.",
                                replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                                cancellationToken: cancellationToken);
                        }

                        break;

                    // Commands that require registration
                    case "🎯 Оғози тест":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        _userScores[chatId] = 0;
                        _userQuestions[chatId] = 0;
                        await HandleNewQuestionAsync(chatId, questionService, subjectService, cancellationToken);
                        break;

                    case "📚 Интихоби фан":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        var subjectKeyboard = new ReplyKeyboardMarkup(new[]
                        {
                            new[] { new KeyboardButton("🧪 Химия"), new KeyboardButton("🔬 Биология") },
                            new[] { new KeyboardButton("📖 Забони тоҷикӣ"), new KeyboardButton("🌍 Забони англисӣ") },
                            new[] { new KeyboardButton("📜 Таърих"), new KeyboardButton("🌍 География") },
                            new[] { new KeyboardButton("📚 Адабиёти тоҷик"), new KeyboardButton("⚛️ Физика") },
                            new[] { new KeyboardButton("🇷🇺 Забони русӣ"), new KeyboardButton("📐 Математика") },
                            new[] { new KeyboardButton("🫀 Анатомия"), new KeyboardButton("⚖️ Ҳуқуқи инсон") },
                            new[] { new KeyboardButton("🧬 Генетика") },
                            new[] { new KeyboardButton("⬅️ Бозгашт") }
                        })
                        {
                            ResizeKeyboard = true
                        };
                        await _client.SendMessage(chatId, "Лутфан, фанро интихоб кунед:",
                            replyMarkup: subjectKeyboard,
                            cancellationToken: cancellationToken);
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
                    case "🫀 Анатомия":
                    case "⚖️ Ҳуқуқи инсон":
                    case "🧬 Генетика":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        await HandleSubjectSelectionAsync(chatId, text, cancellationToken);
                        break;

                    case "⬅️ Бозгашт":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        await _client.SendMessage(chatId, "Бозгашт ба менюи асосӣ",
                            replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                            cancellationToken: cancellationToken);
                        break;

                    case "👨‍💼 Админ":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        await HandleAdminCommandAsync(chatId, cancellationToken);
                        break;

                    case "📢 Фиристодани паём":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        if (await IsUserAdminAsync(chatId, cancellationToken))
                        {
                            _pendingBroadcast[chatId] = true;
                            var cancelKeyboard = new ReplyKeyboardMarkup(new[] { new KeyboardButton("❌ Бекор кардан") })
                                { ResizeKeyboard = true };
                            await _client.SendMessage(chatId,
                                "📢 Лутфан, паёмеро, ки ба ҳамаи корбарон фиристода мешавад, ворид кунед:",
                                replyMarkup: cancelKeyboard,
                                cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд паём фиристанд!",
                                cancellationToken: cancellationToken);
                        }

                        break;

                    case "💬 Тамос бо админ":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        var adminButton = new InlineKeyboardMarkup(new[]
                        {
                            new[] { InlineKeyboardButton.WithUrl("💬 Тамос бо админ", "https://t.me/iamnoseh") }
                        });
                        await _client.SendMessage(
                            chatId,
                            "Барои фиристодани савол ё дархост ба админ, ба ин суроға муроҷиат кунед:",
                            replyMarkup: adminButton,
                            cancellationToken: cancellationToken
                        );
                        break;

                    case "👥 Даъвати дӯстон":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        await HandleInviteFriendsAsync(chatId, cancellationToken);
                        break;

                    case "🎮 Мусобиқа":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        await HandleStartDuelAsync(chatId, cancellationToken);
                        break;

                    case "📊 Омор":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        await HandleStatisticsCommandAsync(chatId, scope.ServiceProvider, cancellationToken);
                        break;

                    case "🏆 Беҳтаринҳо":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        await HandleTopCommandAsync(chatId, scope.ServiceProvider, cancellationToken);
                        break;

                    case "👤 Профил":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        await HandleProfileCommandAsync(chatId, scope.ServiceProvider, cancellationToken);
                        break;

                    case "✏️ Ивази ном":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        _pendingNameChange[chatId] = true;
                        await _client.SendMessage(chatId, "Лутфан, номи нави худро ворид кунед:",
                            cancellationToken: cancellationToken);
                        break;

                    case "📚 Китобхона":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        await HandleLibraryCommandAsync(chatId, cancellationToken);
                        break;

                    case "📤 Иловаи китоб":
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        if (await IsUserAdminAsync(chatId, cancellationToken))
                        {
                            _pendingBookNames[chatId] = null;
                            await _client.SendMessage(chatId, "📚 Лутфан, номи китобро ворид кунед:",
                                cancellationToken: cancellationToken);
                        }
                        else
                        {
                            await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд китоб илова кунанд!",
                                cancellationToken: cancellationToken);
                        }

                        break;

                    default:
                        if (!isRegistered)
                        {
                            Console.WriteLine(
                                $"[REGISTRATION] Unregistered user {chatId} sent unknown command: {text}");
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return;
                        }

                        await _client.SendMessage(chatId, "Фармони нодуруст!",
                            cancellationToken: cancellationToken);
                        break;
                }
            }
            catch (Exception ex) when (ex.Message.Contains("chat not found") ||
                                       ex.Message.Contains("user not found") ||
                                       ex.Message.Contains("bot was blocked"))
            {
                // If we can't send messages to the user, they are probably invalid
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
                var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);

                if (user != null)
                {
                    user.IsLeft = true;
                    await dbContext.SaveChangesAsync(cancellationToken);

                    // Remove user after marking them as left
                    dbContext.Users.Remove(user);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                return;
            }
        }
        else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            var callbackQuery = update.CallbackQuery;
            if (callbackQuery == null) return;

            // Immediately answer the callback query to prevent timeout
            await _client.AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken: cancellationToken);

            var chatId = callbackQuery.Message.Chat.Id;

            // Handle book-related callbacks
            if (callbackQuery.Data?.StartsWith("book_") == true ||
                callbackQuery.Data?.StartsWith("delete_book_") == true ||
                callbackQuery.Data == "add_book")
            {
                await HandleBookCallbackAsync(callbackQuery, cancellationToken);
                return;
            }

            // Now call the dedicated handler for all other callback query logic
            using (var scope = _scopeFactory.CreateScope())
            {
                var questionService = scope.ServiceProvider.GetRequiredService<IQuestionService>();
                var responseService = scope.ServiceProvider.GetRequiredService<IResponseService>();
                var subjectService = scope.ServiceProvider.GetRequiredService<ISubjectService>();
                await HandleCallbackQueryAsync(callbackQuery, questionService, responseService, subjectService,
                    cancellationToken);
            }
        }
    }

    private async Task<bool> IsUserRegisteredAsync(long chatId, IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();

        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);

        if (user != null)
        {
            return true;
        }
        else
        {
            var requestContactButton = new KeyboardButton("Рақами телефон") { RequestContact = true };
            var keyboard = new ReplyKeyboardMarkup(new[] { new[] { requestContactButton } })
                { ResizeKeyboard = true, OneTimeKeyboard = true };
            await _client.SendMessage(chatId,
                "Барои сабти ном тугмаи зеринро пахш кунед ва рақами телефони худро фиристед!",
                replyMarkup: keyboard,
                cancellationToken: cancellationToken);
            return false;
        }
    }

    private async Task HandleContactRegistrationAsync(Message message, IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var contact = message.Contact;
        var autoUsername = !string.IsNullOrWhiteSpace(message.Chat.Username)
            ? message.Chat.Username
            : message.Chat.FirstName;

        Console.WriteLine(
            $"[REGISTRATION] Contact received from user {chatId}, Phone: {contact?.PhoneNumber}, Username: {autoUsername}");

        if (!_pendingRegistrations.ContainsKey(chatId))
        {
            Console.WriteLine($"[REGISTRATION] Starting new registration for user {chatId}");
            _pendingRegistrations[chatId] = new RegistrationInfo
                { Contact = contact, AutoUsername = autoUsername, IsNameReceived = false, IsCityReceived = false };
        }
        else
        {
            Console.WriteLine($"[REGISTRATION] Updating contact for user {chatId}");
            _pendingRegistrations[chatId].Contact = contact;
        }

        await _client.SendMessage(chatId, "Ташаккур! Акнун номатонро ворид кунед.",
            replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: cancellationToken);
    }

    private async Task HandleNameRegistrationAsync(long chatId, string name, CancellationToken cancellationToken)
    {
        if (!_pendingRegistrations.ContainsKey(chatId))
        {
            Console.WriteLine($"[REGISTRATION] ERROR: User {chatId} not found in pending registrations");
            await _client.SendMessage(chatId, "Илтимос, аввал рақами телефони худро фиристед!",
                cancellationToken: cancellationToken);
            await SendRegistrationRequestAsync(chatId, cancellationToken);
            return;
        }

        var regInfo = _pendingRegistrations[chatId];
        regInfo.Name = name;
        regInfo.IsNameReceived = true;
        Console.WriteLine($"[REGISTRATION] Name saved for user {chatId}, asking for city");
        await _client.SendMessage(chatId, "Лутфан, шаҳратонро ворид кунед.",
            replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: cancellationToken);
    }

    private async Task HandleCityRegistrationAsync(long chatId, string city, IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        if (!_pendingRegistrations.ContainsKey(chatId))
        {
            Console.WriteLine($"[REGISTRATION] ERROR: User {chatId} not found in pending registrations");
            await _client.SendMessage(chatId, "Илтимос, аввал рақами телефони худро фиристед!",
                cancellationToken: cancellationToken);
            await SendRegistrationRequestAsync(chatId, cancellationToken);
            return;
        }

        var regInfo = _pendingRegistrations[chatId];
        regInfo.City = city;
        regInfo.IsCityReceived = true;

        Console.WriteLine(
            $"[REGISTRATION] Completing registration for user {chatId} - Name: {regInfo.Name}, City: {regInfo.City}, Phone: {regInfo.Contact?.PhoneNumber}");

        try
        {
            var dbContext = serviceProvider.GetRequiredService<DataContext>();
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

            Console.WriteLine($"[REGISTRATION] User {chatId} successfully saved to database");

            var invitation =
                await dbContext.Invitations.FirstOrDefaultAsync(i => i.InviteeChatId == chatId && i.Status == "pending",
                    cancellationToken);
            if (invitation != null)
            {
                Console.WriteLine(
                    $"[REGISTRATION] Found pending invitation for user {chatId} from {invitation.InviterChatId}");
                invitation.Status = "accepted";
                invitation.AcceptedAt = DateTime.UtcNow;
                var inviter =
                    await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == invitation.InviterChatId,
                        cancellationToken);
                if (inviter != null)
                {
                    inviter.Score += 5;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await _client.SendMessage(inviter.ChatId,
                        "🎉 Дӯсти шумо бо пайванди даъват сабти ном шуд! Шумо 5 бал гирифтед!",
                        cancellationToken: cancellationToken);
                    Console.WriteLine($"[REGISTRATION] Inviter {invitation.InviterChatId} received 5 bonus points");
                }
            }

            await _client.SendMessage(chatId,
                "Сабти номи шумо бо муваффақият анҷом ёфт!\nБарои оғози тест тугмаи 'Оғози тест'-ро пахш кунед!",
                replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                cancellationToken: cancellationToken);
            Console.WriteLine($"[REGISTRATION] Registration completed successfully for user {chatId}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REGISTRATION] ERROR during registration for user {chatId}: {ex.Message}");
            await _client.SendMessage(chatId,
                "Хатогӣ ҳангоми сабти маълумот рух дод. Лутфан, баъдтар дубора кӯшиш кунед.",
                cancellationToken: cancellationToken);
        }
        finally
        {
            _pendingRegistrations.Remove(chatId);
            Console.WriteLine($"[REGISTRATION] Removed user {chatId} from pending registrations");
        }
    }

    private async Task<IReplyMarkup> GetMainButtonsAsync(long chatId, CancellationToken cancellationToken)
    {
        var isAdmin = await IsUserAdminAsync(chatId, cancellationToken);
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);
        var buttons = new List<List<KeyboardButton>>
        {
            new() { new KeyboardButton("📚 Интихоби фан"), new KeyboardButton("🎯 Оғози тест") },
            new() { new KeyboardButton("🏆 Беҳтаринҳо"), new KeyboardButton("👤 Профил") },
            new() { new KeyboardButton("🎮 Мусобиқа"), new KeyboardButton("💬 Тамос бо админ") },
            new() { new KeyboardButton("👥 Даъвати дӯстон"), new KeyboardButton("ℹ️ Кӯмак") },
            new() { new KeyboardButton("📚 Китобхона") }
        };
        if (user != null && !user.HasChangedName)
        {
            buttons[1].Add(new KeyboardButton("✏️ Ивази ном"));
        }

        if (isAdmin)
        {
            buttons.Add(new() { new KeyboardButton("📊 Омор") });
            buttons.Add(new() { new KeyboardButton("👨‍💼 Админ") });
        }

        return new ReplyKeyboardMarkup(buttons) { ResizeKeyboard = true };
    }

    private IReplyMarkup GetButtons(int questionId)
    {
        return new InlineKeyboardMarkup(new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData("▫️ A", $"{questionId}_A"),
                InlineKeyboardButton.WithCallbackData("▫️ B", $"{questionId}_B")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData("▫️ C", $"{questionId}_C"),
                InlineKeyboardButton.WithCallbackData("▫️ D", $"{questionId}_D")
            }
        });
    }

    private async Task HandleSubjectSelectionAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        int subjectId = text switch
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
            "🫀 Анатомия" => 11,
            "⚖️ Ҳуқуқи инсон" => 12,
            "🧬 Генетика" => 13,
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

    private async Task HandleNewQuestionAsync(long chatId, IQuestionService questionService,
        ISubjectService subjectService, CancellationToken cancellationToken)
    {
        if (!_userCurrentSubject.TryGetValue(chatId, out int currentSubject))
        {
            await _client.SendMessage(chatId, "❌ Лутфан, аввал фанро интихоб кунед!",
                replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                cancellationToken: cancellationToken);
            return;
        }

        if (_userQuestions[chatId] >= MaxQuestions)
        {
            string resultText = $"<b>📝 Тест ба охир расид!</b>\nХолҳои шумо: {_userScores[chatId]}/{MaxQuestions}.";
            var restartButton =
                new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("♻️ Аз нав оғоз кунед!", "restart"));
            await _client.SendMessage(chatId, resultText, parseMode: ParseMode.Html, replyMarkup: restartButton,
                cancellationToken: cancellationToken);
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
            }

            var markup = GetButtons(question.QuestionId);
            var messageText = $"<b>📚 Фан: {question.SubjectName}</b>\n\n" +
                              $"❓ {question.QuestionText}\n\n" +
                              $"A) {question.FirstOption}\n" +
                              $"B) {question.SecondOption}\n" +
                              $"C) {question.ThirdOption}\n" +
                              $"D) {question.FourthOption}";

            if (!NoTimerSubjects.Contains(currentSubject))
            {
                messageText += $"\n\n<i>⏱ Вақт: {QuestionTimeLimit} сония</i>";
            }

            var sentMessage = await _client.SendMessage(chatId,
                messageText,
                parseMode: ParseMode.Html,
                replyMarkup: markup,
                cancellationToken: cancellationToken);
            _activeQuestions[chatId] = (question.QuestionId, DateTime.UtcNow, false, markup, sentMessage.MessageId);

            if (!NoTimerSubjects.Contains(currentSubject))
            {
                var cts = new CancellationTokenSource();
                _questionTimers[chatId] = cts;
                _ = UpdateQuestionTimer(chatId, cts.Token);
            }
        }
        else
        {
            await _client.SendMessage(chatId, "❌ Дар айни замон саволҳо барои ин фан дастрас нестанд.",
                cancellationToken: cancellationToken);
        }
    }

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
                        await _client.EditMessageText(
                            chatId: new ChatId(chatId),
                            messageId: updatedInfo.MessageId,
                            text: $"<b>📚 Фан: {question.SubjectName}</b>\n\n" +
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
                    var updatedMarkup =
                        UpdateButtonsMarkup(finalInfo.QuestionId, null, false, question.Answer, question);
                    await _client.EditMessageReplyMarkup(chatId: new ChatId(chatId), messageId: finalInfo.MessageId,
                        replyMarkup: updatedMarkup, cancellationToken: cancellationToken);

                    var userResponse = new UserResponse
                    {
                        ChatId = chatId,
                        QuestionId = finalInfo.QuestionId,
                        SelectedOption = "Ҷавоб дода нашуд",
                        IsCorrect = false
                    };
                    await responseService.SaveUserResponse(userResponse);
                    _activeQuestions[chatId] = (finalInfo.QuestionId, finalInfo.StartTime, true, finalInfo.Markup,
                        finalInfo.MessageId);

                    if (_userQuestions[chatId] < MaxQuestions)
                    {
                        var subjectService = scope.ServiceProvider.GetRequiredService<ISubjectService>();
                        await HandleNewQuestionAsync(chatId, questionService, subjectService, cancellationToken);
                    }
                    else
                    {
                        string resultText =
                            $"<b>📝 Тест ба охир расид!</b>\nХолҳои шумо: {_userScores[chatId]}/{MaxQuestions}.";
                        var restartButton =
                            new InlineKeyboardMarkup(
                                InlineKeyboardButton.WithCallbackData("♻️ Аз нав оғоз кунед!", "restart"));
                        await _client.SendMessage(chatId, resultText, parseMode: ParseMode.Html,
                            replyMarkup: restartButton, cancellationToken: cancellationToken);
                    }
                }
            }
        }
        catch (TaskCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ дар таймер: {ex.Message}");
        }
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, IQuestionService questionService,
        IResponseService responseService, ISubjectService subjectService, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message.Chat.Id;
        var data = callbackQuery.Data; // Get data once

        if (data == "check_subscription")
        {
            await CheckChannelSubscriptionAsync(chatId, cancellationToken);
            return;
        }

        // Handle duel invites and other duel-specific callbacks
        if (data?.StartsWith("duel_") == true)
        {
            var parts = data.Split('_');
            if (parts.Length >= 3)
            {
                var action = parts[1];
                var inviterChatId = long.Parse(parts[2]);
                var subjectId = parts.Length > 3 ? int.Parse(parts[3]) : 0;

                using var duelScope = _scopeFactory.CreateScope();
                var dbContext = duelScope.ServiceProvider.GetRequiredService<DataContext>();
                if (action == "accept")
                {
                    var subject = await dbContext.Subjects
                        .Include(s => s.Questions)
                        .FirstOrDefaultAsync(s => s.Id == subjectId, cancellationToken);
                    if (subject == null)
                    {
                        await _client.SendMessage(chatId, "❌ Хатогӣ: Фан ёфт нашуд!",
                            cancellationToken: cancellationToken);
                        return;
                    }

                    var game = new DuelGame
                    {
                        Player1ChatId = inviterChatId,
                        Player2ChatId = chatId,
                        SubjectId = subjectId,
                        Subject = subject,
                        IsFinished = false,
                        CurrentRound = 1,
                        Player1Score = 0,
                        Player2Score = 0,
                        CreatedAt = DateTime.UtcNow
                    };
                    dbContext.DuelGames.Add(game);
                    await dbContext.SaveChangesAsync(cancellationToken);

                    _activeGames[game.Id] = game;

                    await HandleDuelGameAsync(game, cancellationToken);

                    await _client.SendMessage(inviterChatId, "🎮 Бозингар даъвати шуморо қабул кард! Бозӣ оғоз шуд!",
                        cancellationToken: cancellationToken);
                    await _client.SendMessage(chatId, "🎮 Шумо даъватро қабул кардед! Бозӣ оғоз шуд!",
                        cancellationToken: cancellationToken);
                }
                else if (action == "reject")
                {
                    await _client.SendMessage(inviterChatId, "❌ Бозингар даъвати шуморо рад кард.",
                        cancellationToken: cancellationToken);
                }

                return; // Return after handling duel specific callbacks
            }
        }

        if (!_activeQuestions.TryGetValue(chatId, out var questionInfo) || questionInfo.IsAnswered)
        {
            // Callback was already answered by HandleUpdateAsync, just return
            return;
        }

        var callbackData = data?.Split('_'); // Use the 'data' variable
        if (callbackData == null || callbackData.Length < 2 || !int.TryParse(callbackData[0], out int questionId))
            return;

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
        bool isCorrect = string.Equals(selectedOptionText, correctAnswer, StringComparison.OrdinalIgnoreCase);

        var activeGame =
            _activeGames.Values.FirstOrDefault(g => g.Player1ChatId == chatId || g.Player2ChatId == chatId);
        if (activeGame != null)
        {
            var elapsedSeconds = (DateTime.UtcNow - questionInfo.StartTime).TotalSeconds;
            var timeBonus = Math.Max(0, 1 - (elapsedSeconds / QuestionTimeLimit));

            _activeQuestions[chatId] = (questionId, questionInfo.StartTime, true, questionInfo.Markup,
                questionInfo.MessageId);
            await HandleDuelAnswer(activeGame, chatId, selectedOptionText, isCorrect, timeBonus, cancellationToken);

            var updatedMarkup = UpdateButtonsMarkup(questionId, selectedOption, isCorrect, correctAnswer, question);
            await _client.EditMessageReplyMarkup(chatId: new ChatId(chatId), messageId: callbackQuery.Message.MessageId,
                replyMarkup: updatedMarkup, cancellationToken: cancellationToken);
        }
        else
        {
            _activeQuestions[chatId] = (questionId, questionInfo.StartTime, true, questionInfo.Markup,
                questionInfo.MessageId);

            if (_questionTimers.TryGetValue(chatId, out var currentTimer))
            {
                currentTimer.Cancel();
                _questionTimers.Remove(chatId);
            }

            var updatedMarkup = UpdateButtonsMarkup(questionId, selectedOption, isCorrect, correctAnswer, question);
            await _client.EditMessageReplyMarkup(chatId: new ChatId(chatId), messageId: questionInfo.MessageId,
                replyMarkup: updatedMarkup, cancellationToken: cancellationToken);

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

            var userResponse = new UserResponse
            {
                ChatId = chatId, QuestionId = questionId, SelectedOption = selectedOptionText, IsCorrect = isCorrect
            };
            await responseService.SaveUserResponse(userResponse);

            if (_userQuestions[chatId] < MaxQuestions)
            {
                await HandleNewQuestionAsync(chatId, questionService, subjectService, cancellationToken);
            }
            else
            {
                string resultText =
                    $"<b>📝 Тест ба охир расид!</b>\nХолҳои шумо: {_userScores[chatId]}/{MaxQuestions}.";
                var restartButton =
                    new InlineKeyboardMarkup(InlineKeyboardButton.WithCallbackData("♻️ Аз нав оғоз кунед!", "restart"));
                await _client.SendMessage(chatId, resultText, parseMode: ParseMode.Html, replyMarkup: restartButton,
                    cancellationToken: cancellationToken);
            }
        }
    }

    private InlineKeyboardMarkup UpdateButtonsMarkup(int questionId, string selectedOption, bool isCorrect,
        string correctAnswer, GetQuestionWithOptionsDTO question)
    {
        var buttons = new List<InlineKeyboardButton[]>();

        string correctOption = question.FirstOption.Trim().Equals(correctAnswer, StringComparison.OrdinalIgnoreCase)
            ? "A"
            : question.SecondOption.Trim().Equals(correctAnswer, StringComparison.OrdinalIgnoreCase)
                ? "B"
                : question.ThirdOption.Trim().Equals(correctAnswer, StringComparison.OrdinalIgnoreCase)
                    ? "C"
                    : "D";

        var row1 = new List<InlineKeyboardButton>();
        var row2 = new List<InlineKeyboardButton>();

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

    private async Task<UserCheckResult> IsUserChannelMemberAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            try
            {
                // Try to get user info first
                var userInfo = await _client.GetChat(chatId, cancellationToken);
                if (userInfo == null)
                {
                    Console.WriteLine($"Корбар {chatId} ёфт нашуд ё бастааст (UserInfo null)");
                    return UserCheckResult.InvalidUserOrBlocked;
                }

                // Try to send a chat action to verify user is accessible
                await _client.SendChatAction(chatId, ChatAction.Typing, cancellationToken: cancellationToken);

                // Try to get channel member info
                try
                {
                    // First try to get channel info
                    var channelInfo = await _client.GetChat(_channelId, cancellationToken);
                    if (channelInfo == null)
                    {
                        Console.WriteLine($"Канал ёфт нашуд: {_channelId}");
                        return UserCheckResult.OtherError;
                    }

                    // Try to get channel member info
                    var chatMember = await _client.GetChatMember(_channelId, chatId, cancellationToken);
                    var isMember = chatMember.Status is ChatMemberStatus.Member or
                        ChatMemberStatus.Administrator or
                        ChatMemberStatus.Creator;

                    if (isMember)
                    {
                        // If user is a member, update their status in database if they exist
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
                        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId,
                            cancellationToken);
                        if (user != null)
                        {
                            user.IsLeft = false;
                            await dbContext.SaveChangesAsync(cancellationToken);
                        }

                        Console.WriteLine($"Корбар {chatId} аъзои канал аст");
                        return UserCheckResult.Success;
                    }
                    else
                    {
                        Console.WriteLine($"Корбар {chatId} аъзои канал нест. Вазъият: {chatMember.Status}");
                        return UserCheckResult.NotMember;
                    }
                }
                catch (Exception ex) when (ex.Message.Contains("PARTICIPANT_ID_INVALID") ||
                                           ex.Message.Contains("invalid user_id"))
                {
                    Console.WriteLine($"Хатогии PARTICIPANT_ID_INVALID барои корбар {chatId}");
                    Console.WriteLine($"Навъи хатогӣ: {ex.GetType().Name}");
                    Console.WriteLine($"Матни пурраи хатогӣ: {ex}");

                    // If we get PARTICIPANT_ID_INVALID, the user is likely invalid or blocked.
                    // Mark them as left and remove from database if they exist.
                    using var scope = _scopeFactory.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
                    var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);
                    if (user != null)
                    {
                        user.IsLeft = true;
                        await dbContext.SaveChangesAsync(cancellationToken);
                        dbContext.Users.Remove(user);
                        await dbContext.SaveChangesAsync(cancellationToken);
                        Console.WriteLine($"Корбар {chatId} ҳамчун ғайрифаъол қайд карда шуд ва нест карда шуд.");
                    }

                    return UserCheckResult.InvalidUserOrBlocked;
                }
            }
            catch (Exception ex) when (ex.Message.Contains("user not found") ||
                                       ex.Message.Contains("chat not found") ||
                                       ex.Message.Contains("invalid user_id") ||
                                       ex.Message.Contains("bot was blocked"))
            {
                Console.WriteLine($"Корбар ёфт нашуд ё ботро бастааст: {ex.Message}");
                Console.WriteLine($"Навъи хатогӣ (internal): {ex.GetType().Name}");
                Console.WriteLine($"Матни пурраи хатогӣ (internal): {ex}");
                // If user is not found or has blocked the bot, mark them as left and remove from database if they exist
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
                var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);
                if (user != null)
                {
                    user.IsLeft = true;
                    await dbContext.SaveChangesAsync(cancellationToken);
                    dbContext.Users.Remove(user);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                return UserCheckResult.InvalidUserOrBlocked;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ дар санҷиши корбар {chatId}: {ex.Message}");
            Console.WriteLine($"Навъи хатогӣ: {ex.GetType().Name}");
            Console.WriteLine($"Матни пурраи хатогӣ: {ex}");
            return UserCheckResult.OtherError;
        }
    }

    private async Task<bool> CheckChannelSubscriptionAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            // First check if we can send messages to this user
            await _client.SendChatAction(chatId, ChatAction.Typing, cancellationToken: cancellationToken);

            var checkResult = await IsUserChannelMemberAsync(chatId, cancellationToken);

            switch (checkResult)
            {
                case UserCheckResult.Success:
                    // User is subscribed, check if they need to register
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var isRegistered =
                            await IsUserRegisteredAsync(chatId, scope.ServiceProvider, cancellationToken);
                        if (!isRegistered)
                        {
                            await SendRegistrationRequestAsync(chatId, cancellationToken);
                            return false;
                        }

                        return true;
                    }

                case UserCheckResult.NotMember:
                {
                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithUrl("Обуна шудан ба канал", _channelLink) },
                        new[] { InlineKeyboardButton.WithCallbackData("🔄 Санҷиш", "check_subscription") }
                    });

                    await _client.SendMessage(
                        chatId,
                        "⚠️ Барои истифодаи бот, аввал ба канали мо обуна шавед!\n\n" +
                        "Пас аз обуна шудан, тугмаи '🔄 Санҷиш'-ро пахш кунед.",
                        replyMarkup: keyboard,
                        cancellationToken: cancellationToken
                    );
                    return false;
                }

                case UserCheckResult.InvalidUserOrBlocked:
                {
                    var keyboard = new InlineKeyboardMarkup(new[]
                    {
                        new[] { InlineKeyboardButton.WithCallbackData("🔄 Аз нав оғоз кардан", "/start") }
                    });

                    await _client.SendMessage(
                        chatId,
                        "⚠️ Мутаассифона, ҳисоби шумо дастрас нест ё баста шудааст. Лутфан, ботро аз нав оғоз кунед.\n\n" +
                        "Тугмаи '🔄 Аз нав оғоз кардан'-ро пахш кунед ё фармони /start-ро фиристед.",
                        replyMarkup: keyboard,
                        cancellationToken: cancellationToken
                    );
                    return false;
                }

                case UserCheckResult.OtherError:
                default:
                {
                    Console.WriteLine($"Хатогии номаълум ҳангоми санҷиши обуна барои корбар {chatId}");
                    await _client.SendMessage(
                        chatId,
                        "❌ Хатогӣ ҳангоми санҷиши обунаи шумо рух дод. Лутфан, баъдтар кӯшиш кунед.",
                        cancellationToken: cancellationToken
                    );
                    return false;
                }
            }
        }
        catch (Exception ex) when (ex.Message.Contains("chat not found") ||
                                   ex.Message.Contains("user not found") ||
                                   ex.Message.Contains("bot was blocked"))
        {
            Console.WriteLine($"Хатогӣ дар санҷиши обуна: {ex.Message}");
            // If we can't send messages to the user, they are probably invalid
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
            var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);

            if (user != null)
            {
                user.IsLeft = true;
                await dbContext.SaveChangesAsync(cancellationToken);
                dbContext.Users.Remove(user);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            return false;
        }
    }

    private async Task HandleTopCommandAsync(long chatId, IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var dbContext = serviceProvider.GetRequiredService<DataContext>();
        var topUsers = await dbContext.Users.OrderByDescending(u => u.Score).Take(50).ToListAsync(cancellationToken);
        if (topUsers.Count == 0)
        {
            await _client.SendMessage(chatId, "Рӯйхат холист!", cancellationToken: cancellationToken);
            return;
        }

        string GetLevelStars(int level) => new string('⭐', level);
        string GetRankColor(int rank) => rank switch { 1 => "🥇", 2 => "🥈", 3 => "🥉", <= 10 => "🔹", _ => "⚪" };
        int cnt = 0;
        var messageText =
            "<b>🏆 50 Беҳтарин</b>\n\n<pre>#        Ном ва насаб         Хол  </pre>\n<pre>----------------------------------</pre>\n";
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

    private async Task HandleProfileCommandAsync(long chatId, IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var dbContext = serviceProvider.GetRequiredService<DataContext>();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);
        if (user != null)
        {
            int level = GetLevel(user.Score);
            // Calculate user rank
            var allScores = await dbContext.Users.OrderByDescending(u => u.Score).Select(u => u.ChatId)
                .ToListAsync(cancellationToken);
            int rank = allScores.IndexOf(chatId) + 1;
            string profileText =
                $"<b>Профил:</b>\n    {user.Name}\n<b>Шаҳр:</b> {user.City}\n<b>Хол:</b> {user.Score}\n<b>Сатҳ:</b> {level}\n<b>Рейтинг:</b> {rank}-ум";
            await _client.SendMessage(chatId, profileText, parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
        }
        else
        {
            await _client.SendMessage(chatId,
                "Шумо ҳанӯз сабти ном нашудаед. Барои сабти ном /register -ро пахш кунед.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleHelpCommandAsync(long chatId, CancellationToken cancellationToken)
    {
        string helpText =
            "<b>Роҳнамо:</b>\n/start - оғоз ва санҷиши сабти ном\n/register - сабти номи ҳисоби корбар\nОғози тест - барои оғози тест\nБеҳтаринҳо - дидани 50 корбари беҳтарин\nПрофил - дидани маълумоти шахсии шумо\nКӯмак - дидани ин рӯйхат\n";
        await _client.SendMessage(chatId, helpText, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
    }

    private int GetLevel(int score) => score switch { <= 150 => 1, <= 300 => 2, <= 450 => 3, <= 600 => 4, _ => 5 };

    private void CleanupBroadcastState(long chatId)
    {
        _pendingBroadcast.Remove(chatId);
    }

    private async Task HandleBroadcastMessageAsync(long chatId, string messageText, IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await IsUserAdminAsync(chatId, cancellationToken))
            {
                CleanupBroadcastState(chatId);
                await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд паём фиристанд!",
                    replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                    cancellationToken: cancellationToken);
                return;
            }

            if (string.IsNullOrWhiteSpace(messageText))
            {
                await _client.SendMessage(chatId, "❌ Паём наметавонад холӣ бошад! Лутфан, паёми дигар ворид кунед.",
                    cancellationToken: cancellationToken);
                return;
            }

            var dbContext = serviceProvider.GetRequiredService<DataContext>();
            var users = await dbContext.Users.Select(u => u.ChatId).ToListAsync(cancellationToken);

            if (users.Count == 0)
            {
                CleanupBroadcastState(chatId);
                await _client.SendMessage(chatId, "❌ Дар ҳоли ҳозир ягон корбар барои фиристодани паём нест.",
                    replyMarkup: GetAdminButtons(), cancellationToken: cancellationToken);
                return;
            }

            var statusMessage = await _client.SendMessage(chatId,
                $"<b>📤 Фиристодани паём оғоз шуд...</b>\n0/{users.Count} корбарон", parseMode: ParseMode.Html,
                replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
            var successCount = 0;
            var failedCount = 0;
            var blockedCount = 0;
            var lastUpdateTime = DateTime.UtcNow;
            var batchSize = 30;
            var removedUsers = new List<User>();

            for (var i = 0; i < users.Count; i += batchSize)
            {
                var batch = users.Skip(i).Take(batchSize);
                foreach (var userId in batch)
                {
                    try
                    {
                        if (await IsUserBlockedBotAsync(userId, cancellationToken))
                        {
                            var userToDelete =
                                await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == userId, cancellationToken);
                            if (userToDelete != null && !removedUsers.Contains(userToDelete))
                            {
                                removedUsers.Add(userToDelete);
                                dbContext.Users.Remove(userToDelete);
                                await dbContext.SaveChangesAsync(cancellationToken);
                                Console.WriteLine(
                                    $"Корбар {userId} аз пойгоҳи додаҳо нест карда шуд (бот заблок карда шудааст)");
                            }

                            blockedCount++;
                            continue;
                        }

                        await _client.SendMessage(userId, $"<b>📢 Паёми муҳим:</b>\n\n{messageText}",
                            parseMode: ParseMode.Html, cancellationToken: cancellationToken);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Хатогӣ дар фиристодан ба корбар {userId}: {ex.Message}");
                        failedCount++;
                    }

                    if ((DateTime.UtcNow - lastUpdateTime).TotalSeconds >= 3 || (i + batchSize) >= users.Count)
                    {
                        try
                        {
                            var progress = (double)(successCount + failedCount + blockedCount) / users.Count * 100;
                            var progressBar = MakeProgressBar(progress);
                            var progressText = $"<b>📤 Фиристодани паём идома дорад...</b>\n{progressBar}\n" +
                                               $"✅ Бо муваффақият: {successCount}\n" +
                                               $"❌ Ноком: {failedCount}\n" +
                                               $"🚫 Корбарони блоккардаи бот: {blockedCount}\n" +
                                               $"📊 Пешрафт: {progress:F1}%";

                            await _client.EditMessageText(
                                chatId: new ChatId(chatId),
                                messageId: statusMessage.MessageId,
                                text: progressText,
                                parseMode: ParseMode.Html,
                                cancellationToken: cancellationToken);

                            lastUpdateTime = DateTime.UtcNow;
                        }
                        catch (Exception ex) when (ex.Message.Contains("message can't be edited"))
                        {
                            statusMessage = await _client.SendMessage(
                                chatId,
                                $"<b>📤 Фиристодани паём идома дорад...</b>\n" +
                                $"✅ Бо муваффақият: {successCount}\n" +
                                $"❌ Ноком: {failedCount}\n" +
                                $"🚫 Корбарони блоккардаи бот: {blockedCount}",
                                parseMode: ParseMode.Html,
                                cancellationToken: cancellationToken);
                            lastUpdateTime = DateTime.UtcNow;
                        }

                        lastUpdateTime = DateTime.UtcNow;
                    }
                }

                await Task.Delay(500, cancellationToken);
            }

            var resultMessage = $"<b>📬 Фиристодани паём ба итмом расид!</b>\n\n" +
                                $"✅ Бо муваффақият фиристода шуд: {successCount}\n" +
                                $"❌ Ноком: {failedCount}\n" +
                                $"🚫 Корбарони блоккардаи бот: {blockedCount}\n" +
                                $"📊 Фоизи муваффақият: {((double)successCount / (users.Count - blockedCount) * 100):F1}%";

            await _client.SendMessage(chatId, resultMessage, parseMode: ParseMode.Html, replyMarkup: GetAdminButtons(),
                cancellationToken: cancellationToken);
            await NotifyAdminsAsync(
                $"<b>📢 Натиҷаи фиристодани паёми оммавӣ:</b>\n\n{resultMessage}\n\n🕒 Вақт: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
                cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ дар идоракунии паём: {ex}");
            await _client.SendMessage(chatId, "❌ Хатогӣ ҳангоми коркарди паём. Лутфан боз кӯшиш кунед.",
                replyMarkup: GetAdminButtons(), cancellationToken: cancellationToken);
        }
        finally
        {
            CleanupBroadcastState(chatId);
        }
    }

    private async Task HandleStatisticsCommandAsync(long chatId, IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        // Check if the user is an admin
        if (!await IsUserAdminAsync(chatId, cancellationToken))
        {
            await _client.SendMessage(chatId, "❌ Шумо иҷозати дидани оморро надоред!",
                cancellationToken: cancellationToken);
            return;
        }

        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();

        // Get total number of users
        var totalUsers = await dbContext.Users.CountAsync(cancellationToken);
        var activeUsers = await dbContext.UserResponses
            .Where(r => r.CreatedAt >= DateTime.UtcNow.AddDays(-7))
            .Select(r => r.ChatId)
            .Distinct()
            .CountAsync(cancellationToken);

        var subjects = await dbContext.Subjects.ToListAsync(cancellationToken);
        var questionCounts = await dbContext.Questions
            .GroupBy(q => q.SubjectId)
            .Select(g => new { SubjectId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.SubjectId, g => g.Count, cancellationToken);

        var totalQuestions = await dbContext.Questions.CountAsync(cancellationToken);

        var subjectStats = subjects
            .OrderByDescending(s => questionCounts.GetValueOrDefault(s.Id, 0))
            .Select(s =>
            {
                var count = questionCounts.GetValueOrDefault(s.Id, 0);
                var emoji = s.Name switch
                {
                    "Химия" => "🧪",
                    "Биология" => "🔬",
                    "Забони тоҷикӣ" => "📖",
                    "English" => "🌍",
                    "Таърих" => "📜",
                    "География" => "🌍",
                    "Адабиёти тоҷик" => "📚",
                    "Физика" => "⚛️",
                    "Забони русӣ" => "🇷🇺",
                    "Математика" => "📐",
                    "Анатомия" => "🫀",
                    "Ҳуқуқи инсон" => "⚖️",
                    "Генетика" => "🧬",
                    _ => "📚"
                };
                return $"• {emoji} {s.Name}: {count:N0} савол";
            })
            .ToList();

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

        await _client.SendMessage(
            chatId,
            statsMessage,
            parseMode: ParseMode.Html,
            replyMarkup: GetAdminButtons(),
            cancellationToken: cancellationToken
        );
    }

    private async Task HandleFileUploadAsync(Message message, IQuestionService questionService,
        ISubjectService subjectService, CancellationToken cancellationToken)
    {
        if (message.Document == null) return;
        var chatId = message.Chat.Id;
        var fileName = message.Document.FileName ?? "бе ном";
        var username = !string.IsNullOrWhiteSpace(message.From?.Username)
            ? $"@{message.From.Username}"
            : message.From?.FirstName ?? "Корбари номаълум";
        // Accept both .docx and .pdf
        if (!fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) &&
            !fileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            await _client.SendMessage(chatId, "❌ Лутфан, танҳо файли .docx ё .pdf фиристед!",
                cancellationToken: cancellationToken);
            return;
        }

        try
        {
            var file = await _client.GetFile(message.Document.FileId, cancellationToken);
            if (file.FilePath == null) throw new Exception("Гирифтани роҳи файл аз Telegram ғайримумкин аст");
            using var stream = new MemoryStream();
            await _client.DownloadFile(file.FilePath, stream, cancellationToken);
            stream.Position = 0;
            if (!_userCurrentSubject.TryGetValue(chatId, out int currentSubject))
            {
                await _client.SendMessage(chatId, "❌ Лутфан, аввал фанро интихоб кунед!",
                    cancellationToken: cancellationToken);
                return;
            }

            await NotifyAdminsAsync($"<b>📥 Файли нав аз {username}</b>\nНоми файл: {fileName}\nДар ҳоли коркард...",
                cancellationToken);
            // Only .docx is parsed for questions, .pdf support must be implemented in ParseQuestionsDocx
            var questions = ParseQuestionsDocx.ParseQuestionsFromDocx(stream, currentSubject);
            int added = 0, duplicate = 0, error = 0;
            var errorMessages = new List<string>();
            foreach (var question in questions)
            {
                try
                {
                    var result = await questionService.CreateQuestion(question);
                    if (result != null) added++;
                    else duplicate++;
                }
                catch (Exception ex)
                {
                    error++;
                    errorMessages.Add($"{error}. {question.QuestionText} — {ex.Message}");
                }
            }

            // Get subject name
            var subject = await subjectService.GetSubjectById(currentSubject);
            string subjectName = subject?.Name ?? "";
            string errorList = errorMessages.Count > 0 ? string.Join("\n", errorMessages) : "-";
            var summary =
                $"<b>📚 Фан:</b> {subjectName}\n<b>✅ Саволҳои нав:</b> {added}\n<b>♻️ Такрорӣ:</b> {duplicate}\n<b>❌ Хатогӣ:</b> {error}\n{errorList}\n<b>Ҷамъ:</b> {questions.Count}";
            await _client.SendMessage(chatId, summary, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
            await NotifyAdminsAsync(
                $"<b>✅ Аз файли {fileName}</b>\nАз ҷониби {username} фиристода шуд,\n{added} савол нав, {duplicate} такрорӣ, {error} хатогӣ, фан: {subjectName}",
                cancellationToken);
        }
        catch (Exception ex)
        {
            var errorMessage = $"<b>❌ Хатогӣ:</b> {ex.Message}";
            await _client.SendMessage(chatId, errorMessage, parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
            await NotifyAdminsAsync(
                $"<b>❌ Хатогӣ ҳангоми коркарди файл:</b>\nФайл: {fileName}\nКорбар: {username}\nХатогӣ: {ex.Message}",
                cancellationToken);
        }
    }

    private async Task NotifyAdminsAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            var chatMembers = await _client.GetChatAdministrators(_channelId, cancellationToken);
            foreach (var member in chatMembers)
            {
                if (member.Status is ChatMemberStatus.Creator or ChatMemberStatus.Administrator)
                {
                    try
                    {
                        await _client.SendMessage(member.User.Id, message, parseMode: ParseMode.Html,
                            cancellationToken: cancellationToken);
                    }
                    catch (Exception)
                    {
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ ҳангоми огоҳ кардани админҳо: {ex.Message}");
        }
    }

    private async Task<bool> IsUserAdminAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var chatMember = await _client.GetChatMember(_channelId, chatId, cancellationToken);
            return chatMember.Status is ChatMemberStatus.Creator or ChatMemberStatus.Administrator;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ ҳангоми санҷиши вазъи админ: {ex.Message}");
            return false;
        }
    }

    private IReplyMarkup GetAdminButtons()
    {
        return new ReplyKeyboardMarkup(new List<List<KeyboardButton>>
        {
            new() { new KeyboardButton("📚 Интихоби фан") },
            new() { new KeyboardButton("📊 Омор"), new KeyboardButton("📝 Саволҳо") },
            new() { new KeyboardButton("📢 Фиристодани паём") },
            new() { new KeyboardButton("📤 Иловаи китоб") },
            new() { new KeyboardButton("⬅️ Бозгашт") }
        }) { ResizeKeyboard = true };
    }

    private async Task HandleAdminCommandAsync(long chatId, CancellationToken cancellationToken)
    {
        var isAdmin = await IsUserAdminAsync(chatId, cancellationToken);
        if (!isAdmin)
        {
            await _client.SendMessage(chatId,
                "❌ Бубахшед, шумо админ нестед!\nБарои админ шудан, ба канал ҳамчун маъмур ё созанда илова шавед.",
                cancellationToken: cancellationToken);
            return;
        }

        await _client.SendMessage(chatId, "Хуш омаед ба панели админ!\nЛутфан, амалро интихоб кунед:",
            replyMarkup: GetAdminButtons(), cancellationToken: cancellationToken);
    }

    private string MakeProgressBar(double percent)
    {
        var filledCount = (int)(percent / 10);
        var emptyCount = 10 - filledCount;
        return $"[{new string('█', filledCount)}{new string('░', emptyCount)}]";
    }

    private async Task<bool> IsUserBlockedBotAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            await _client.SendChatAction(chatId, ChatAction.Typing, cancellationToken: cancellationToken);
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

    private async Task HandleAskAdminAsync(long chatId, string question, CancellationToken cancellationToken)
    {
        try
        {
            if (!await IsUserRegisteredAsync(chatId, _scopeFactory.CreateScope().ServiceProvider, cancellationToken))
            {
                await _client.SendMessage(chatId, "❌ Барои фиристодани савол ба админ бояд аввал сабти ном кунед.",
                    cancellationToken: cancellationToken);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();

            var question2Admin = new Question2Admin
            {
                UserChatId = chatId,
                QuestionText = question,
                IsAnswered = false
            };

            dbContext.QuestionsToAdmin.Add(question2Admin);
            await dbContext.SaveChangesAsync(cancellationToken);

            await _client.SendMessage(chatId,
                "✅ Саволи шумо ба админҳо фиристода шуд. Онҳо дар назди имкон ҷавоб медиҳанд.",
                cancellationToken: cancellationToken);

            await NotifyAdminsAsync(
                $"<b>❓ Саволи нав аз корбар:</b>\n\n{question}\n\nБарои ҷавоб додан: /answer_{question2Admin.Id}",
                cancellationToken);
        }
        catch (Exception ex)
        {
            await _client.SendMessage(chatId, "❌ Хатогӣ ҳангоми фиристодани савол. Лутфан, баъдтар боз кӯшиш кунед.",
                cancellationToken: cancellationToken);
            Console.WriteLine($"Error in HandleAskAdminAsync: {ex.Message}");
        }
    }

    private async Task HandleInviteFriendsAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var botInviteLink = $"https://t.me/{_botUsername}?start=ref_{chatId}";
            string duelInviteLink = "";
            if (_userCurrentSubject.TryGetValue(chatId, out int currentSubject))
            {
                duelInviteLink = $"https://t.me/{_botUsername}?start=duel_{chatId}_{currentSubject}";
            }

            await _client.SendMessage(chatId,
                $"Дӯстони худро даъват кунед!\n\n" +
                $"{botInviteLink}\n" +
                "Пас аз сабти номи дӯстатон, шумо 5 бал мегиред.\n\n" +
                (duelInviteLink != ""
                    ? $"{duelInviteLink}\n" +
                      "Дӯстатонро ба бозии дукаса даъват кунед.\n\n"
                    : "") +
                "ℹ️ Барои фиристодани линк ба дӯстон, онро нусхабардорӣ кунед ва ба чати онҳо фиристед.",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await _client.SendMessage(chatId, "❌ Хатогӣ ҳангоми даъват кардани дӯстон.",
                cancellationToken: cancellationToken);
            Console.WriteLine($"Error in HandleInviteFriendsAsync: {ex.Message}");
        }
    }

    private async Task HandleStartDuelAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            if (!await IsUserRegisteredAsync(chatId, _scopeFactory.CreateScope().ServiceProvider, cancellationToken))
            {
                await _client.SendMessage(chatId, "❌ Барои оғози мусобиқа бояд аввал сабти ном кунед.",
                    cancellationToken: cancellationToken);
                return;
            }

            if (!_userCurrentSubject.TryGetValue(chatId, out int currentSubject))
            {
                await _client.SendMessage(chatId, "❌ Лутфан, аввал фанро интихоб кунед!",
                    cancellationToken: cancellationToken);
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
            var inviteLink = $"https://t.me/{_botUsername}?start=duel_{chatId}_{currentSubject}";
            await _client.SendMessage(chatId,
                "👥 Дӯстони худро ба мусобиқа даъват кунед!\n\n" +
                $"{inviteLink}\n" +
                "Дӯстатонро ба мусобиқа даъват кунед.\n\n" +
                "ℹ️ Барои фиристодани линк ба дӯстон, онро нусхабардорӣ кунед ва ба чати онҳо фиристед.",
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await _client.SendMessage(chatId, "❌ Хатогӣ ҳангоми оғози мусобиқа.", cancellationToken: cancellationToken);
            Console.WriteLine($"Error in HandleStartDuelAsync: {ex.Message}");
        }
    }

    private async Task HandleDuelInviteAsync(long chatId, long inviterChatId, int subjectId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!await IsUserRegisteredAsync(chatId, _scopeFactory.CreateScope().ServiceProvider, cancellationToken))
            {
                await _client.SendMessage(chatId, "❌ Барои иштирок дар мусобиқа бояд аввал сабти ном кунед.",
                    cancellationToken: cancellationToken);
                return;
            }

            var inlineKeyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Қабул", $"duel_accept_{inviterChatId}_{subjectId}"),
                    InlineKeyboardButton.WithCallbackData("❌ Рад", $"duel_reject_{inviterChatId}")
                }
            });

            await _client.SendMessage(chatId,
                "🎮 Шумо даъватномаи мусобиқа гирифтед. Оё мехоҳед, ки ба мусобиқа ҳамроҳ шавед?",
                replyMarkup: inlineKeyboard,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await _client.SendMessage(chatId, "❌ Хатогӣ ҳангоми қабул кардани даъватнома.",
                cancellationToken: cancellationToken);
            Console.WriteLine($"Error in HandleDuelInviteAsync: {ex.Message}");
        }
    }

    private async Task HandleDuelGameAsync(DuelGame game, CancellationToken cancellationToken)
    {
        try
        {
            if (game.CurrentRound > MaxDuelRounds)
            {
                await NotifyDuelEnd(game, cancellationToken);
                return;
            }

            // Cancel any existing timers for both players
            if (_questionTimers.TryGetValue(game.Player1ChatId, out var timer1))
            {
                timer1.Cancel();
                _questionTimers.Remove(game.Player1ChatId);
            }

            if (_questionTimers.TryGetValue(game.Player2ChatId, out var timer2))
            {
                timer2.Cancel();
                _questionTimers.Remove(game.Player2ChatId);
            }

            using var scope = _scopeFactory.CreateScope();
            var questionService = scope.ServiceProvider.GetRequiredService<IQuestionService>();
            var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();

            var dbGame = await dbContext.DuelGames.FindAsync(new object[] { game.Id }, cancellationToken);
            if (dbGame != null)
            {
                dbGame.CurrentRound = game.CurrentRound;
                dbGame.Player1Score = game.Player1Score;
                dbGame.Player2Score = game.Player2Score;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var question = await questionService.GetRandomQuestionBySubject(game.SubjectId);
            if (question == null)
            {
                await NotifyDuelEnd(game, cancellationToken);
                return;
            }

            var markup = GetButtons(question.QuestionId);
            var baseMessageText = $"<b>🎮 Мусобиқа</b> (Савол {game.CurrentRound}/{MaxDuelRounds})\n\n" +
                                  $"<b>📚 Фан:</b> {question.SubjectName}\n\n" +
                                  $"❓ {question.QuestionText}\n\n" +
                                  $"A) {question.FirstOption}\n" +
                                  $"B) {question.SecondOption}\n" +
                                  $"C) {question.ThirdOption}\n" +
                                  $"D) {question.FourthOption}\n\n";

            var scoreText = $"\n\nХолҳо: {game.Player1Score}:{game.Player2Score}";
            var messageText = baseMessageText + $"⏱ Вақт: {QuestionTimeLimit} сония" + scoreText;

            var msg1 = await _client.SendMessage(game.Player1ChatId, messageText + scoreText, parseMode: ParseMode.Html,
                replyMarkup: markup, cancellationToken: cancellationToken);
            var msg2 = await _client.SendMessage(game.Player2ChatId, messageText + scoreText, parseMode: ParseMode.Html,
                replyMarkup: markup, cancellationToken: cancellationToken);

            _activeQuestions[game.Player1ChatId] =
                (question.QuestionId, DateTime.UtcNow, false, markup, msg1.MessageId);
            _activeQuestions[game.Player2ChatId] =
                (question.QuestionId, DateTime.UtcNow, false, markup, msg2.MessageId);
            var cts = new CancellationTokenSource();
            var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token);

            _questionTimers[game.Player1ChatId] = cts;
            _questionTimers[game.Player2ChatId] = cts;
            _ = Task.Run(async () =>
            {
                try
                {
                    var remainingTime = QuestionTimeLimit;
                    while (remainingTime > 0)
                    {
                        await Task.Delay(1000, linkedCts.Token); // Wait 1 second
                        remainingTime--;

                        // Update timer for both players if they haven't answered
                        if (!_activeQuestions[game.Player1ChatId].IsAnswered)
                        {
                            var msg1 = baseMessageText + $"⏱ Вақт: {remainingTime} сония" + scoreText;
                            try
                            {
                                await _client.EditMessageText(
                                    chatId: new ChatId(game.Player1ChatId),
                                    messageId: _activeQuestions[game.Player1ChatId].MessageId,
                                    text: msg1,
                                    parseMode: ParseMode.Html,
                                    replyMarkup: (InlineKeyboardMarkup)_activeQuestions[game.Player1ChatId].Markup);
                            }
                            catch (Exception)
                            {
                            }
                        }

                        if (!_activeQuestions[game.Player2ChatId].IsAnswered)
                        {
                            var msg2 = baseMessageText + $"⏱ Вақт: {remainingTime} сония" + scoreText;
                            try
                            {
                                await _client.EditMessageText(
                                    chatId: new ChatId(game.Player2ChatId),
                                    messageId: _activeQuestions[game.Player2ChatId].MessageId,
                                    text: msg2,
                                    parseMode: ParseMode.Html,
                                    replyMarkup: (InlineKeyboardMarkup)_activeQuestions[game.Player2ChatId].Markup);
                            }
                            catch (Exception)
                            {
                            }
                        }

                        // If both players answered, break the loop
                        if (_activeQuestions[game.Player1ChatId].IsAnswered &&
                            _activeQuestions[game.Player2ChatId].IsAnswered)
                        {
                            break;
                        }
                    }

                    // If time ran out and at least one player hasn't answered
                    if (remainingTime <= 0 &&
                        (!_activeQuestions[game.Player1ChatId].IsAnswered ||
                         !_activeQuestions[game.Player2ChatId].IsAnswered))
                    {
                        await HandleDuelQuestionTimeout(game.Id, question.QuestionId, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    linkedCts.Dispose();
                }
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in HandleDuelGameAsync: {ex.Message}");
        }
    }

    private async Task HandleDuelAnswer(DuelGame game, long playerChatId, string selectedOption, bool isCorrect,
        double timeBonus, CancellationToken cancellationToken)
    {
        var score = isCorrect ? BaseScore + (int)(timeBonus * SpeedBonus) : 0;

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var dbGame = await dbContext.DuelGames.FindAsync(new object[] { game.Id }, cancellationToken);

        if (dbGame != null)
        {
            if (playerChatId == game.Player1ChatId)
            {
                game.Player1Score += score;
                dbGame.Player1Score = game.Player1Score;
            }
            else
            {
                game.Player2Score += score;
                dbGame.Player2Score = game.Player2Score;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var otherPlayerId = playerChatId == game.Player1ChatId ? game.Player2ChatId : game.Player1ChatId;

        if (_activeQuestions.TryGetValue(otherPlayerId, out var otherQuestion) && otherQuestion.IsAnswered)
        {
            game.CurrentRound++;
            await dbContext.SaveChangesAsync(cancellationToken);
            await HandleDuelGameAsync(game, cancellationToken);
        }
    }

    private async Task NotifyDuelEnd(DuelGame game, CancellationToken cancellationToken)
    {
        game.IsFinished = true;
        game.FinishedAt = DateTime.UtcNow;

        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var player1 = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == game.Player1ChatId, cancellationToken);
        var player2 = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == game.Player2ChatId, cancellationToken);

        string resultMessage;
        if (game.Player1Score == game.Player2Score)
        {
            resultMessage = $"🤝 Мусобиқа бо натиҷаи баробар ба анҷом расид!\n\n" +
                            $"Натиҷа: {game.Player1Score}:{game.Player2Score}\n\n" +
                            $"Бозингарон:\n" +
                            $"👤 {player1?.Name}: {game.Player1Score} хол\n" +
                            $"👤 {player2?.Name}: {game.Player2Score} хол";
        }
        else
        {
            var winner = game.Player1Score > game.Player2Score ? player1 : player2;
            var winnerScore = Math.Max(game.Player1Score, game.Player2Score);
            var loserScore = Math.Min(game.Player1Score, game.Player2Score);

            if (winner != null)
            {
                winner.Score += 3;
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            resultMessage = $"🏆 Ғолиби мусобиқа муайян шуд!\n\n" +
                            $"🎉 Табрик ба {winner?.Name}!\n\n" +
                            $"Натиҷа: {winnerScore}:{loserScore}\n" +
                            $"(Ғолиб 3 холи иловагӣ гирифт)";
        }

        await _client.SendMessage(game.Player1ChatId, resultMessage, cancellationToken: cancellationToken);
        await _client.SendMessage(game.Player2ChatId, resultMessage, cancellationToken: cancellationToken);

        var dbGame = await dbContext.DuelGames.FindAsync(new object[] { game.Id }, cancellationToken);
        if (dbGame != null)
        {
            dbGame.IsFinished = true;
            dbGame.FinishedAt = game.FinishedAt;
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        _activeGames.Remove(game.Id);
    }

    private async Task HandleDuelQuestionTimeout(int gameId, int questionId, CancellationToken cancellationToken)
    {
        if (_activeGames.TryGetValue(gameId, out var game))
        {
            // Make sure both players' timers are cleaned up
            if (_questionTimers.TryGetValue(game.Player1ChatId, out var timer1))
            {
                _questionTimers.Remove(game.Player1ChatId);
            }

            if (_questionTimers.TryGetValue(game.Player2ChatId, out var timer2))
            {
                _questionTimers.Remove(game.Player2ChatId);
            }

            var question = await _scopeFactory.CreateScope().ServiceProvider
                .GetRequiredService<IQuestionService>()
                .GetQuestionById(questionId);

            if (question != null)
            {
                if (_activeQuestions.TryGetValue(game.Player1ChatId, out var p1Question) && !p1Question.IsAnswered)
                {
                    await _client.SendMessage(game.Player1ChatId,
                        $"⏱ Вақт тамом шуд! Ҷавоби дуруст: {question.Answer}",
                        cancellationToken: cancellationToken);
                    _activeQuestions[game.Player1ChatId] = (p1Question.QuestionId, p1Question.StartTime, true,
                        p1Question.Markup, p1Question.MessageId);
                }

                if (_activeQuestions.TryGetValue(game.Player2ChatId, out var p2Question) && !p2Question.IsAnswered)
                {
                    await _client.SendMessage(game.Player2ChatId,
                        $"⏱ Вақт тамом шуд! Ҷавоби дуруст: {question.Answer}",
                        cancellationToken: cancellationToken);
                    _activeQuestions[game.Player2ChatId] = (p2Question.QuestionId, p2Question.StartTime, true,
                        p2Question.Markup, p2Question.MessageId);
                }
            }

            if (_activeQuestions[game.Player1ChatId].IsAnswered && _activeQuestions[game.Player2ChatId].IsAnswered)
            {
                game.CurrentRound++;
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
                var dbGame = await dbContext.DuelGames.FindAsync(new object[] { game.Id }, cancellationToken);
                if (dbGame != null)
                {
                    dbGame.CurrentRound = game.CurrentRound;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                await HandleDuelGameAsync(game, cancellationToken);
            }
        }
    }

    private async Task SendRegistrationRequestAsync(long chatId, CancellationToken cancellationToken)
    {
        if (chatId < 0) // Group or channel
        {
            await _client.SendMessage(chatId, "Барои сабти ном ба бот дар private chat нависед!",
                cancellationToken: cancellationToken);
            return;
        }

        var requestContactButton = new KeyboardButton("Рақами телефон") { RequestContact = true };
        var keyboard = new ReplyKeyboardMarkup(new[] { new[] { requestContactButton } })
            { ResizeKeyboard = true, OneTimeKeyboard = true };
        await _client.SendMessage(chatId,
            "Барои сабти ном тугмаи зеринро пахш кунед ва рақами телефони худро фиристед!", replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private IBookService GetBookService()
    {
        if (_bookService == null)
        {
            var scope = _scopeFactory.CreateScope();
            _bookService = scope.ServiceProvider.GetRequiredService<IBookService>();
        }

        return _bookService;
    }

    private async Task HandleLibraryCommandAsync(long chatId, CancellationToken cancellationToken)
    {
        var bookService = GetBookService();
        var books = await bookService.GetAllBooksAsync();
        if (books.Count == 0)
        {
            await _client.SendMessage(chatId, "📚 Дар ҳоли ҳозир китобхона холӣ аст.",
                cancellationToken: cancellationToken);
            return;
        }

        var isAdmin = await IsUserAdminAsync(chatId, cancellationToken);
        var message = "📚 Китобҳои мавҷуд:\n\n";
        var inlineKeyboard = new List<List<InlineKeyboardButton>>();

        foreach (var book in books)
        {
            var fileSize = book.FileSize > 1024 * 1024
                ? $"{book.FileSize / (1024.0 * 1024.0):F1} MB"
                : $"{book.FileSize / 1024.0:F1} KB";

            // Намоиши ном, ҳаҷм, сол ва тавсиф
            message += $"📖 <b>Ном:</b> {book.Title}\n" +
                       $"📦 <b>Ҳаҷм:</b> {fileSize}\n" +
                       $"📅 <b>Сол:</b> {(book.Year )}\n" +
                       $"📝 <b>Тавсиф:</b> {(string.IsNullOrWhiteSpace(book.Description) ? "Тавсиф мавҷуд нест" : book.Description)}\n" +
                       $"🕒 <b>Санаи илова:</b> {book.UploadedAt:dd.MM.yyyy}\n\n";

            var row = new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData($"📥 Боргирии {book.Title}", $"book_{book.Id}")
            };

            if (isAdmin)
            {
                row.Add(InlineKeyboardButton.WithCallbackData($"❌ Нест кардани {book.Title}",
                    $"delete_book_{book.Id}"));
            }

            inlineKeyboard.Add(row);
        }

        if (isAdmin)
        {
            inlineKeyboard.Add(new List<InlineKeyboardButton>
            {
                InlineKeyboardButton.WithCallbackData("📤 Иловаи китоб", "add_book")
            });
        }

        await _client.SendMessage(
            chatId,
            message,
            parseMode: ParseMode.Html, // Барои форматкунии матн бо тегҳои HTML
            replyMarkup: new InlineKeyboardMarkup(inlineKeyboard),
            cancellationToken: cancellationToken
        );
    }

    private async Task HandleBookUploadAsync(Message message, PendingBookInfo info, CancellationToken cancellationToken)
    {
        if (message.Document == null) return;

        var chatId = message.Chat.Id;
        var fileId = message.Document.FileId;
        var fileName = message.Document.FileName ?? "бе ном";
        var fileSize = message.Document.FileSize ?? 0;

        try
        {
            // Download file from Telegram
            var file = await _client.GetFileAsync(fileId, cancellationToken);
            if (file.FilePath == null) throw new Exception("Роҳи файл аз Telegram дастрас нест.");
            var filePath = Path.Combine(_filesDirectory, fileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await _client.DownloadFile(file.FilePath, fileStream, cancellationToken);
            }

            // Get user ID
            var uploadedByUserId = await GetUserIdByChatIdAsync(chatId, cancellationToken);
            if (uploadedByUserId == 0)
            {
                await _client.SendMessage(chatId, "❌ Корбар ёфт нашуд.", cancellationToken: cancellationToken);
                return;
            }

            // Create DTO
            var createBookDto = new CreateBookDTO
            {
                Title = info.Title,
                Description = info.Description,
                CategoryId = 1,
                FileName = fileName,
                FileExtension = Path.GetExtension(fileName).ToLower(),
                FileSize = fileSize,
                UploadedByUserId = uploadedByUserId
            };

            // Save book
            var bookService = GetBookService();
            var book = await bookService.CreateBookAsync(createBookDto, filePath);
            if (book == null)
            {
                await _client.SendMessage(chatId, "❌ Хатогӣ ҳангоми илова кардани китоб.",
                    cancellationToken: cancellationToken);
                return;
            }

            // Success message
            await _client.SendMessage(
                chatId,
                $"✅ Китоб бо муваффақият илова шуд!\n\n" +
                $"📖 Ном: {book.Title}\n" +
                $"📄 Файл: {book.FileName}\n" +
                $"📦 Ҳаҷм: {(book.FileSize > 1024 * 1024 ? $"{book.FileSize / (1024.0 * 1024.0):F1} MB" : $"{book.FileSize / 1024.0:F1} KB")}\n" +
                $"📝 Тавсиф: {book.Description}\n" +
                $"📅 Сол: {info.Year}",
                replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                cancellationToken: cancellationToken
            );
        }
        catch (Exception ex)
        {
            await _client.SendMessage(
                chatId,
                $"❌ Хатогӣ ҳангоми илова кардани китоб: {ex.Message}",
                replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                cancellationToken: cancellationToken
            );
        }
    }

    private async Task HandleBookCallbackAsync(CallbackQuery callbackQuery, CancellationToken cancellationToken)
    {
        var chatId = callbackQuery.Message.Chat.Id;
        var data = callbackQuery.Data;

        if (data.StartsWith("book_"))
        {
            var bookId = int.Parse(data.Split('_')[1]);
            var bookService = GetBookService();
            var book = await bookService.GetBookByIdAsync(bookId);
            if (book == null)
            {
                await _client.SendMessage(chatId, "❌ Китоб ёфт нашуд.", cancellationToken: cancellationToken);
                return;
            }

            var filePath = await bookService.GetBookFilePathAsync(bookId);
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                await _client.SendMessage(chatId, "❌ Файли китоб ёфт нашуд.", cancellationToken: cancellationToken);
                return;
            }

            try
            {
                using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                await _client.SendDocumentAsync(
                    chatId,
                    InputFile.FromStream(fileStream, book.FileName),
                    caption: $"📖 {book.Title}",
                    cancellationToken: cancellationToken
                );

                // Increment download count
                await bookService.IncrementDownloadCountAsync(bookId);
            }
            catch (Exception ex)
            {
                await _client.SendMessage(
                    chatId,
                    $"❌ Хатогӣ ҳангоми фиристодани китоб: {ex.Message}",
                    cancellationToken: cancellationToken
                );
            }
        }
        else if (data.StartsWith("delete_book_"))
        {
            if (!await IsUserAdminAsync(chatId, cancellationToken))
            {
                await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд китобро нест кунанд!",
                    cancellationToken: cancellationToken);
                return;
            }

            var bookId = int.Parse(data.Split('_')[2]);
            var bookService = GetBookService();
            if (await bookService.DeleteBookAsync(bookId))
            {
                await _client.SendMessage(chatId, "✅ Китоб бо муваффақият нест карда шуд.",
                    cancellationToken: cancellationToken);
                await HandleLibraryCommandAsync(chatId, cancellationToken);
            }
            else
            {
                await _client.SendMessage(chatId, "❌ Хатогӣ ҳангоми нест кардани китоб.",
                    cancellationToken: cancellationToken);
            }
        }
        else if (data == "add_book")
        {
            if (!await IsUserAdminAsync(chatId, cancellationToken))
            {
                await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд китоб илова кунанд!",
                    cancellationToken: cancellationToken);
                return;
            }

            _pendingBookNames[chatId] = null;
            await _client.SendMessage(chatId, "📚 Лутфан, номи китобро ворид кунед:",
                cancellationToken: cancellationToken);
        }
    }

    private async Task<int> GetUserIdByChatIdAsync(long chatId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DataContext>();
        var user = await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == chatId, cancellationToken);
        return user?.Id ?? 0;
    }
}