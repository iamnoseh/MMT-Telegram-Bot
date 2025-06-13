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
using TelegramBot.Services.Extensions;
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
    private const string _botUsername = "darsnet_bot";
    private readonly Dictionary<long, RegistrationInfo> _pendingRegistrations = new();
    private readonly Dictionary<long, int> _userScores = new();
    private readonly Dictionary<long, int> _userQuestions = new();
    private readonly Dictionary<long, bool> _pendingBroadcast = new();
    private readonly Dictionary<long, int> _userCurrentSubject = new();

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

    public TelegramBotHostedService(IServiceScopeFactory scopeFactory, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        var token = configuration["BotConfiguration:Token"] ??
                    throw new ArgumentNullException("Токени Боти Telegram ёфт нашуд!");
        _client = new TelegramBotClient(token);
        _channelId = configuration["TelegramChannel:ChannelId"] ??
                     throw new ArgumentNullException("ID-и канал ёфт нашуд!");
        _channelLink = configuration["TelegramChannel:ChannelLink"] ??
                       throw new ArgumentNullException("Пайванди канал ёфт нашуд!");
    }

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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine("Бот қатъ карда мешавад...");
        return Task.CompletedTask;
    }

    private async Task HandleStatisticsCommandAsync(long chatId, IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        try
        {
            var dbContext = serviceProvider.GetRequiredService<DataContext>();
            var totalUsers = await dbContext.Users.CountAsync(cancellationToken);
            var activeUsers = await dbContext.Users.CountAsync(u => u.IsActive && !u.IsLeft, cancellationToken);
            var totalQuestions = await dbContext.Questions.CountAsync(cancellationToken);
            var totalSubjects = await dbContext.Subjects.CountAsync(cancellationToken);
            var totalDuels = await dbContext.DuelGames.CountAsync(cancellationToken);

            string stats = $"<b>📊 Омор:</b>\n" +
                           $"👥 Ҳамагӣ корбарон: {totalUsers}\n" +
                           $"✅ Корбарони фаъол: {activeUsers}\n" +
                           $"❓ Саволҳо: {totalQuestions}\n" +
                           $"📚 Фанҳо: {totalSubjects}\n" +
                           $"🎮 Мусобиқаҳо: {totalDuels}";

            await _client.SendMessage(chatId, stats, parseMode: ParseMode.Html, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            await _client.SendMessage(chatId, "❌ Хатогӣ ҳангоми гирифтани омор.", cancellationToken: cancellationToken);
            Console.WriteLine($"Error in HandleStatisticsCommandAsync: {ex.Message}");
        }
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
                }
            }

            if (_pendingBroadcast.ContainsKey(chatId) && _pendingBroadcast[chatId])
            {
                if (text == "❌ Бекор кардан")
                {
                    CleanupBroadcastState(chatId);
                    await _client.SendMessage(chatId, "Фиристодани паём бекор карда шуд!",
                        replyMarkup: GetAdminButtons(), cancellationToken: cancellationToken);
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
                            "Хуш омаед! Барои оғози тест тугмаи 'Оғози тест'-ро пахш кунед.",
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
                        await _client.SendMessage(chatId, "Шумо аллакай сабти ном шудаед!",
                            cancellationToken: cancellationToken);
                    }

                    break;

                case "🎯 Оғози тест":
                    if (!await IsUserRegisteredAsync(chatId, scope.ServiceProvider, cancellationToken))
                    {
                        await _client.SendMessage(chatId,
                            "Лутфан, аввал дар бот сабти ном кунед. Барои сабти ном /register -ро пахш кунед.",
                            cancellationToken: cancellationToken);
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

                case "📚 Интихоби фан":
                    var subjectKeyboard = new ReplyKeyboardMarkup
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
                    await _client.SendMessage(chatId, "Лутфан, фанро интихоб кунед:", replyMarkup: subjectKeyboard,
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
                    await HandleSubjectSelectionAsync(chatId, text, cancellationToken);
                    break;

                case "⬅️ Бозгашт":
                    await _client.SendMessage(chatId, "Бозгашт ба менюи асосӣ",
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
                        var cancelKeyboard = new ReplyKeyboardMarkup(new[] { new KeyboardButton("❌ Бекор кардан") })
                            { ResizeKeyboard = true };
                        await _client.SendMessage(chatId,
                            "📢 Лутфан, паёмеро, ки ба ҳамаи корбарон фиристода мешавад, ворид кунед:",
                            replyMarkup: cancelKeyboard, cancellationToken: cancellationToken);
                    }
                    else
                    {
                        await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд паём фиристанд!",
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
                        await _client.SendMessage(chatId, "❌ Танҳо админҳо метавонанд оморро бубинанд!",
                            cancellationToken: cancellationToken);
                    }

                    break;
                case "💬 Тамос бо админ":
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
                    await HandleInviteFriendsAsync(chatId, cancellationToken);
                    break;

                case "🎮 Мусобиқа":
                    await HandleStartDuelAsync(chatId, cancellationToken);
                    break;

                default:
                    await _client.SendMessage(chatId, "Фармони нодуруст!", cancellationToken: cancellationToken);
                    break;
            }
        }
        else if (update.Type == UpdateType.CallbackQuery && update.CallbackQuery != null)
        {
            var callbackQuery = update.CallbackQuery;
            var chatId = callbackQuery.Message.Chat.Id;

            if (callbackQuery.Data?.StartsWith("duel_") == true)
            {
                var parts = callbackQuery.Data.Split('_');
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

                        await _client.SendMessage(inviterChatId,
                            "🎮 Бозингар даъвати шуморо қабул кард! Бозӣ оғоз шуд!",
                            cancellationToken: cancellationToken);
                        await _client.SendMessage(chatId, "🎮 Шумо даъватро қабул кардед! Бозӣ оғоз шуд!",
                            cancellationToken: cancellationToken);
                    }
                    else if (action == "reject")
                    {
                        await _client.SendMessage(inviterChatId, "❌ Бозингар даъвати шуморо рад кард.",
                            cancellationToken: cancellationToken);
                    }

                    await _client.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
                    return;
                }
            }

            using var scope = _scopeFactory.CreateScope();
            var questionService = scope.ServiceProvider.GetRequiredService<IQuestionService>();
            var responseService = scope.ServiceProvider.GetRequiredService<IResponseService>();
            var subjectService = scope.ServiceProvider.GetRequiredService<ISubjectService>();
            await HandleCallbackQueryAsync(callbackQuery, questionService, responseService, subjectService,
                cancellationToken);
        }
    }


    private async Task<bool> IsUserRegisteredAsync(long chatId, IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var dbContext = serviceProvider.GetRequiredService<DataContext>();
        return await dbContext.Users.AnyAsync(u => u.ChatId == chatId, cancellationToken);
    }

    private async Task SendRegistrationRequestAsync(long chatId, CancellationToken cancellationToken)
    {
        var requestContactButton = new KeyboardButton("Рақами телефон") { RequestContact = true };
        var keyboard = new ReplyKeyboardMarkup(new[] { new[] { requestContactButton } })
            { ResizeKeyboard = true, OneTimeKeyboard = true };
        await _client.SendMessage(chatId, "Барои сабти ном тугмаи зеринро пахш кунед!", replyMarkup: keyboard,
            cancellationToken: cancellationToken);
    }

    private async Task HandleContactRegistrationAsync(Message message, IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var contact = message.Contact;
        var autoUsername = !string.IsNullOrWhiteSpace(message.Chat.Username)
            ? message.Chat.Username
            : message.Chat.FirstName;

        if (!_pendingRegistrations.ContainsKey(chatId))
        {
            _pendingRegistrations[chatId] = new RegistrationInfo
                { Contact = contact, AutoUsername = autoUsername, IsNameReceived = false, IsCityReceived = false };
            await _client.SendMessage(chatId, "Ташаккур! Акнун номатонро ворид кунед.",
                replyMarkup: new ReplyKeyboardRemove(), cancellationToken: cancellationToken);
        }
        else
        {
            await _client.SendMessage(chatId, "Лутфан, номатонро ворид кунед, то сабти номро анҷом диҳед.",
                cancellationToken: cancellationToken);
        }
    }

    private async Task HandleNameRegistrationAsync(long chatId, string name, CancellationToken cancellationToken)
    {
        if (!_pendingRegistrations.ContainsKey(chatId)) return;
        var regInfo = _pendingRegistrations[chatId];
        regInfo.Name = name;
        regInfo.IsNameReceived = true;
        await _client.SendMessage(chatId, "Лутфан, шаҳратонро ворид кунед.", replyMarkup: new ReplyKeyboardRemove(),
            cancellationToken: cancellationToken);
    }

    private async Task HandleCityRegistrationAsync(long chatId, string city, IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        if (!_pendingRegistrations.ContainsKey(chatId)) return;
        var regInfo = _pendingRegistrations[chatId];
        regInfo.City = city;
        regInfo.IsCityReceived = true;

        try
        {
            var dbContext = serviceProvider.GetRequiredService<DataContext>();
            var user = new User
            {
                ChatId = chatId, Username = regInfo.AutoUsername, Name = regInfo.Name,
                PhoneNumber = regInfo.Contact.PhoneNumber, City = regInfo.City, Score = 0
            };
            dbContext.Users.Add(user);
            await dbContext.SaveChangesAsync(cancellationToken);

            // Санҷиш ва қабули даъват
            var invitation =
                await dbContext.Invitations.FirstOrDefaultAsync(i => i.InviteeChatId == chatId && i.Status == "pending",
                    cancellationToken);
            if (invitation != null)
            {
                invitation.Status = "accepted";
                invitation.AcceptedAt = DateTime.UtcNow;
                var inviter =
                    await dbContext.Users.FirstOrDefaultAsync(u => u.ChatId == invitation.InviterChatId,
                        cancellationToken);
                if (inviter != null)
                {
                    inviter.Score += 5; // Илова кардани 5 бал ба даъваткунанда
                    await dbContext.SaveChangesAsync(cancellationToken);
                    await _client.SendMessage(inviter.ChatId,
                        "🎉 Дӯсти шумо бо пайванди даъват сабти ном шуд! Шумо 5 бал гирифтед!",
                        cancellationToken: cancellationToken);
                }
            }

            await _client.SendMessage(chatId,
                "Сабти номи шумо бо муваффақият анҷом ёфт!\nБарои оғози тест тугмаи 'Оғози тест'-ро пахш кунед!",
                replyMarkup: await GetMainButtonsAsync(chatId, cancellationToken),
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ ҳангоми сабти корбар: {ex.Message}");
            await _client.SendMessage(chatId,
                "Хатогӣ ҳангоми сабти маълумот рух дод. Лутфан, баъдтар дубора кӯшиш кунед.",
                cancellationToken: cancellationToken);
        }
        finally
        {
            _pendingRegistrations.Remove(chatId);
        }
    }

    private async Task<IReplyMarkup> GetMainButtonsAsync(long chatId, CancellationToken cancellationToken)
    {
        var isAdmin = await IsUserAdminAsync(chatId, cancellationToken);
        var buttons = new List<List<KeyboardButton>>
        {
            new() { new KeyboardButton("📚 Интихоби фан"), new KeyboardButton("🎯 Оғози тест") },
            new() { new KeyboardButton("🏆 Беҳтаринҳо"), new KeyboardButton("👤 Профил") },
            new() { new KeyboardButton("🎮 Мусобиқа"), new KeyboardButton("💬 Тамос бо админ") },
            new() { new KeyboardButton("👥 Даъвати дӯстон"), new KeyboardButton("ℹ️ Кӯмак") }
        };
        if (isAdmin) buttons.Add(new() { new KeyboardButton("👨‍💼 Админ") });
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

        if (!_activeQuestions.TryGetValue(chatId, out var questionInfo) || questionInfo.IsAnswered)
        {
            await _client.AnswerCallbackQuery(callbackQuery.Id, "⚠️ Вақти ҷавоб додан гузашт!", showAlert: true,
                cancellationToken: cancellationToken);
            return;
        }

        var callbackData = callbackQuery.Data?.Split('_');
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

        await _client.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: cancellationToken);
    }

    private InlineKeyboardMarkup UpdateButtonsMarkup(int questionId, string selectedOption, bool isCorrect,
        string correctAnswer, GetQuestionWithOptionsDTO question)
    {
        var buttons = new List<InlineKeyboardButton[]>();

        string correctOption = question.FirstOption.Trim().Equals(correctAnswer, StringComparison.OrdinalIgnoreCase)
            ?
            "A"
            :
            question.SecondOption.Trim().Equals(correctAnswer, StringComparison.OrdinalIgnoreCase)
                ? "B"
                :
                question.ThirdOption.Trim().Equals(correctAnswer, StringComparison.OrdinalIgnoreCase)
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

    private async Task<bool> IsUserChannelMemberAsync(long chatId, CancellationToken cancellationToken)
    {
        try
        {
            var chatMember = await _client.GetChatMember(_channelId, chatId, cancellationToken);
            return chatMember.Status is ChatMemberStatus.Member or ChatMemberStatus.Administrator
                or ChatMemberStatus.Creator;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Хатогӣ ҳангоми санҷиши аъзогии канал: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> CheckChannelSubscriptionAsync(long chatId, CancellationToken cancellationToken)
    {
        if (!await IsUserChannelMemberAsync(chatId, cancellationToken))
        {
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[] { InlineKeyboardButton.WithUrl("Обуна шудан ба канал", _channelLink) },
                new[] { InlineKeyboardButton.WithCallbackData("🔄 Санҷиш", "check_subscription") }
            });
            await _client.SendMessage(chatId, "⚠️ Барои истифодаи бот, аввал ба канали мо обуна шавед!",
                replyMarkup: keyboard, cancellationToken: cancellationToken);
            return false;
        }

        return true;
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
            string profileText =
                $"<b>Профил:</b>\n    {user.Name}\n<b>Шаҳр:</b> {user.City}\n<b>Хол:</b> {user.Score}\n<b>Сатҳ:</b> {level}";
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

    private async Task HandleBroadcastMessageAsync(long chatId, string? messageText, IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(messageText))
        {
            await _client.SendMessage(chatId, "❌ Паём набояд холӣ бошад!", cancellationToken: cancellationToken);
            CleanupBroadcastState(chatId);
            return;
        }

        var dbContext = serviceProvider.GetRequiredService<DataContext>();
        var users = await dbContext.Users
            .Where(u => u.IsActive && !u.IsLeft)
            .Select(u => u.ChatId)
            .ToListAsync(cancellationToken);

        int successCount = 0;
        int blockedCount = 0;
        int totalUsers = users.Count;

        await _client.SendMessage(chatId, $"🚀 Оғози фиристодани паём ба {totalUsers} корбар...",
            cancellationToken: cancellationToken);

        foreach (var userChatId in users)
        {
            try
            {
                if (await _client.HandleBlockedUser(serviceProvider, userChatId, cancellationToken))
                {
                    blockedCount++;
                    continue;
                }

                await _client.SendMessage(userChatId, messageText, cancellationToken: cancellationToken);
                successCount++;

                // Add a small delay to avoid hitting rate limits
                await Task.Delay(50, cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error sending broadcast message to {userChatId}: {ex.Message}");
            }
        }

        var summaryMessage = $"📊 Натиҷаи фиристодани паём:\n" +
                             $"✅ Бомуваффақият: {successCount}\n" +
                             $"❌ Корбарони блоккарда: {blockedCount}\n" +
                             $"📝 Ҳамагӣ: {totalUsers}";

        await _client.SendMessage(chatId, summaryMessage, cancellationToken: cancellationToken);
        CleanupBroadcastState(chatId);
    }

    private void CleanupBroadcastState(long chatId)
    {
        if (_pendingBroadcast.ContainsKey(chatId))
        {
            _pendingBroadcast.Remove(chatId);
        }
    }

    private async Task HandleFileUploadAsync(Message message, IQuestionService questionService,
        ISubjectService subjectService, CancellationToken cancellationToken)
    {
        if (message.Document == null) return;
        var chatId = message.Chat.Id;
        var fileName = message.Document.FileName ?? "бе ном.docx";
        var username = !string.IsNullOrWhiteSpace(message.From?.Username)
            ? $"@{message.From.Username}"
            : message.From?.FirstName ?? "Корбари номаълум";
        if (!fileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
        {
            await _client.SendMessage(chatId, "❌ Лутфан, танҳо файли .docx фиристед!",
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
            var questions = ParseQuestionsDocx.ParseQuestionsFromDocx(stream, currentSubject);
            foreach (var question in questions) await questionService.CreateQuestion(question);
            var successMessage = $"<b>✅ {questions.Count} савол бо муваффақият илова шуд!</b>";
            await _client.SendMessage(chatId, successMessage, parseMode: ParseMode.Html,
                cancellationToken: cancellationToken);
            await NotifyAdminsAsync(
                $"<b>✅ Аз файли {fileName}</b>\nАз ҷониби {username} фиристода шуд,\n{questions.Count} савол бо муваффақият илова шуд!",
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
                "👥 Дӯстонро ба мусобиқа даъват кунед!\n\n" +
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
}