using MediatR;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;

namespace MMT.TelegramBot.Services;

public class TelegramBotHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TelegramBotHostedService> _logger;
    private readonly Configuration.BotConfiguration _botConfig;
    private readonly ITelegramBotClient _botClient;

    public TelegramBotHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<TelegramBotHostedService> logger,
        IOptions<Configuration.BotConfiguration> botConfigOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _botConfig = botConfigOptions.Value;

        if (string.IsNullOrEmpty(_botConfig.Token))
            throw new InvalidOperationException("Bot token not configured in appsettings.json");

        _botClient = new TelegramBotClient(_botConfig.Token);
        
        _logger.LogInformation("TelegramBot initialized with Channel: {ChannelId}", _botConfig.ChannelId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telegram Bot Service started");

        try
        {
            var me = await _botClient.GetMe(stoppingToken);
            _logger.LogInformation("Bot started: @{BotUsername}", me.Username);

            _botClient.StartReceiving(
                updateHandler: HandleUpdateAsync,
                errorHandler: HandleErrorAsync,
                cancellationToken: stoppingToken
            );

            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Telegram Bot Service");
            throw;
        }
    }

    private async Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            _logger.LogDebug("Received update {UpdateId}, Type: {UpdateType}", update.Id, update.Type);

            if (update.Message != null)
            {
                await HandleMessageAsync(update.Message, mediator, cancellationToken);
            }
            else if (update.CallbackQuery != null)
            {
                await HandleCallbackQueryAsync(update.CallbackQuery, mediator, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update {UpdateId}", update.Id);
        }
    }
    
    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, IMediator mediator, CancellationToken ct)
    {
        var chatId = callbackQuery.Message!.Chat.Id;
        var data = callbackQuery.Data;
        
        _logger.LogInformation("Callback from {ChatId}: {Data}", chatId, data);
        
        await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
        
        if (data?.StartsWith("answer_") == true)
        {
            var messageId = callbackQuery.Message.MessageId;
            await HandleAnswerCallbackAsync(chatId, messageId, data, mediator, ct);
        }
        else if (data?.StartsWith("download_book_") == true)
        {
            var bookIdStr = data.Replace("download_book_", "");
            if (int.TryParse(bookIdStr, out var bookId))
            {
                await HandleBookDownloadAsync(chatId, $"/book{bookId}", mediator, ct);
            }
        }
        else if (data?.StartsWith("import_subject_") == true)
        {
            await HandleImportSubjectCallbackAsync(chatId, data, mediator, ct);
        }
    }
    
    private async Task HandleMessageAsync(Message message, IMediator mediator, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        
        _logger.LogInformation("Message from {ChatId}: Text={Text}, HasContact={HasContact}", 
            chatId, message.Text, message.Contact != null);
        
        if (message.Contact != null)
        {
            await HandleContactAsync(message, mediator, ct);
            return;
        }
        
        if (message.Document != null)
        {
            await HandleDocumentAsync(message, mediator, ct);
            return;
        }
        
        if (string.IsNullOrEmpty(message.Text)) return;
        
        var text = message.Text;
        
        if (text == "/start" || text.StartsWith("/start "))
        {
            await HandleStartCommandAsync(chatId, message.From, text, mediator, ct);
        }
        else
        {
            await HandleTextMessageAsync(chatId, text, mediator, ct);
        }
    }
    
    private async Task HandleStartCommandAsync(long chatId, User? from, string text, IMediator mediator, CancellationToken ct)
    {
        
        string? referralCode = null;
        string? duelCode = null;
        
        if (text.StartsWith("/start ref_"))
        {
            referralCode = text.Replace("/start ref_", "").Trim();
            _logger.LogInformation("Referral code detected: {Code} for user {ChatId}", referralCode, chatId);
        }
        else if (text.StartsWith("/start duel_"))
        {
            duelCode = text.Replace("/start duel_", "").Trim();
            _logger.LogInformation("Duel code detected: {Code} for user {ChatId}", duelCode, chatId);
        }

        var command = new Application.Features.Bot.Commands.HandleStart.HandleStartCommand
        {
            ChatId = chatId,
            Username = from?.Username,
            FirstName = from?.FirstName,
            ReferralCode = referralCode
        };
        
        var result = await mediator.Send(command, ct);
        
        if (result.ShouldRequestPhone)
        {
            var keyboard = new ReplyKeyboardMarkup(new[]
            {
                new KeyboardButton("📱 Фиристодани рақами телефон") { RequestContact = true }
            })
            {
                ResizeKeyboard = true
            };
            
            await _botClient.SendMessage(chatId, result.Message, replyMarkup: keyboard, cancellationToken: ct);
        }
        else
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
            var user = await unitOfWork.Users.GetByChatIdAsync(chatId, ct);
            
            var mainKeyboard = GetMainMenuKeyboard(user);
            await _botClient.SendMessage(chatId, result.Message, replyMarkup: mainKeyboard, cancellationToken: ct);
            
            if (!string.IsNullOrEmpty(duelCode))
            {
                await HandleDuelInvitationAsync(chatId, duelCode, mediator, ct);
            }
        }
    }
    
    private async Task HandleContactAsync(Message message, IMediator mediator, CancellationToken ct)
    {
        var command = new Application.Features.Bot.Commands.HandlePhoneRegistration.HandlePhoneRegistrationCommand
        {
            ChatId = message.Chat.Id,
            PhoneNumber = message.Contact!.PhoneNumber,
            Username = message.From?.Username,
            FirstName = message.From?.FirstName
        };
        
        var result = await mediator.Send(command, ct);
        
        var keyboard = new ReplyKeyboardMarkup(new KeyboardButton("Main menu"))
        {
            ResizeKeyboard = true
        };
        
        await _botClient.SendMessage(message.Chat.Id, result.Message, replyMarkup: keyboard, cancellationToken: ct);
    }
    
    private async Task HandleTextMessageAsync(long chatId, string text, IMediator mediator, CancellationToken ct)
    {
        if (text is "⬅️ Бозгашт" or "⬅️ Бекор кардан")
        {
            await HandleBackButtonAsync(chatId, mediator, ct);
            return;
        }
        
        var session = await GetRegistrationSessionAsync(chatId, mediator, ct);
        
        if (session != null)
        {
            _logger.LogInformation("Active registration session found for {ChatId}, Step: {Step}", 
                chatId, session.CurrentStep);
            await HandleRegistrationFlowAsync(chatId, text, session, mediator, ct);
            return;
        }
        
        _logger.LogInformation("No active session, checking other commands for {ChatId}: {Text}", chatId, text);
        if (text.StartsWith("/setadmin"))
        {
            await HandleSetAdminCommandAsync(chatId, text, mediator, ct);
            return;
        }
        
        if (text == "📚 Интихоби фан")
        {
            await ShowSubjectSelectionAsync(chatId, mediator, ct);
            return;
        }
        
        if (text == "🎯 Оғози тест")
        {
            await HandleStartTestAsync(chatId, mediator, ct);
            return;
        }
        
        if (text == "📚 Китобхона")
        {
            await HandleLibraryAsync(chatId, mediator, ct);
            return;
        }
        
        if (text == "👥 Даъвати дӯстон")
        {
            await HandleReferralAsync(chatId, mediator, ct);
            return;
        }
        
        if (text == "👤 Профил")
        {
            await HandleProfileAsync(chatId, mediator, ct);
            return;
        }
        
        if (text == "🏆 Беҳтаринҳо")
        {
            await HandleLeaderboardAsync(chatId, mediator, ct);
            return;
        }
        
        if (text == "⚔️ Дуэл")
        {
            await HandleDuelRequestAsync(chatId, mediator, ct);
            return;
        }
        
        if (text == "📊 Статистика")
        {
            await HandleStatisticsAsync(chatId, mediator, ct);
            return;
        }
        
        if (text == "📢 Паём фиристодан")
        {
            await HandleBroadcastPromptAsync(chatId, mediator, ct);
            return;
        }
        
        if (text == "📥 Дохил кардани саволҳо")
        {
            await HandleQuestionImportRequestAsync(chatId, mediator, ct);
            return;
        }
        
        if (text.StartsWith("/book"))
        {
            await HandleBookDownloadAsync(chatId, text, mediator, ct);
            return;
        }

        if (text == "📤 Боргузории китоб")
        {
            await StartBookUploadAsync(chatId, mediator, ct);
            return;
        }
        
        var userState = await GetUserStateAsync(chatId, mediator, ct);
        if (userState?.IsPendingBroadcast == true)
        {
            await HandleBroadcastMessageAsync(chatId, text, mediator, ct);
            return;
        }
        
        if (userState?.BookUploadStep != null)
        {
            await HandleBookUploadFlowAsync(chatId, text, userState, mediator, ct);
            return;
        }
        
        if (text == "🎯 Оғози тест")
        {
            await ShowSubjectSelectionAsync(chatId, mediator, ct);
        }
        else if (text.StartsWith("📚 "))
        {
            await HandleSubjectSelectionAsync(chatId, text, mediator, ct);
        }
        else if (text == "⬅️ Бозгашт")
        {
            var mainKeyboard = GetMainMenuKeyboard();
            await _botClient.SendMessage(chatId, "Бозгашт ба менюи асосӣ", 
                replyMarkup: mainKeyboard, cancellationToken: ct);
        }
        else
        {
            _logger.LogInformation("Unhandled message from {ChatId}: {Text}", chatId, text);
        }
    }
    
    private async Task HandleReferralAsync(long chatId, IMediator mediator, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new Application.Features.Referrals.Queries.GetReferralLink.GetReferralLinkQuery
            {
                ChatId = chatId,
                BotUsername = _botConfig.Username
            }, ct);
            
            if (string.IsNullOrEmpty(result.ReferralCode))
            {
                await _botClient.SendMessage(chatId,
                    "Хатогӣ рух дод. Лутфан боз кӯшиш кунед.",
                    cancellationToken: ct);
                return;
            }
            
            var message = $"🎁 **Даъвати дӯстон**\n\n" +
                         $"Дӯстони худро даъват кунед!\n\n" +
                         $"🔗 Линки шумо:\n`{result.ReferralLink}`\n\n" +
                         $"👥 Дӯстони даъватшуда: **{result.TotalReferrals}**\n\n" +
                         $"Линкро ба дӯстон фиристед!";
            
            await _botClient.SendMessage(chatId,
                message,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling referral for {ChatId}", chatId);
        }
    }
    
    private async Task HandleProfileAsync(long chatId, IMediator mediator, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new Application.Features.Users.Queries.GetUserProfile.GetUserProfileQuery
            {
                ChatId = chatId
            }, ct);
            
            if (result == null)
            {
                await _botClient.SendMessage(chatId,
                    "Профили шумо ёфт нашуд.",
                    cancellationToken: ct);
                return;
            }
            
            var message = $"👤 **Профили шумо**\n\n" +
                         $"📛 Ном: {result.Name}\n" +
                         $"🏙 Шаҳр: {result.City}\n" +
                         $"🏆 Холҳо: {result.Score}\n" +
                         $"📊 Мавқеъ: #{result.Rank}\n\n" +
                         $"━━━━━━━━━━━━━━━━━ \n" +
                         $"🏆 **Ҳамаи холҳо:** {result.Score}\n" +
                         $"   ├ 🎯 Аз саволҷавоб: {result.QuizPoints}\n" +
                         $"   └ 🎁 Аз рефералҳо: {result.ReferralPoints}\n\\n" +
                         $"👥 Дӯстони даъватшуда: {result.ReferralCount}";
            
            await _botClient.SendMessage(chatId,
                message,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling profile for {ChatId}", chatId);
            await _botClient.SendMessage(chatId,
                "Хатогӣ рух дод.",
                cancellationToken: ct);
        }
    }
    
    private async Task HandleLeaderboardAsync(long chatId, IMediator mediator, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new Application.Features.Users.Queries.GetTopUsers.GetTopUsersQuery
            {
                Count = 30
            }, ct);
            
            if (result.Count == 0)
            {
                await _botClient.SendMessage(chatId,
                    "Ҷадвали беҳтаринҳо холӣ аст.",
                    cancellationToken: ct);
                return;
            }
            
            var message = "🏆 **Беҳтаринҳо** (Топ-30)\n\n";
            
            for (int i = 0; i < result.Count; i++)
            {
                var user = result[i];
                var medal = i switch
                {
                    0 => "🥇",
                    1 => "🥈",
                    2 => "🥉",
                    _ => $"{i + 1}."
                };
                
                message += $"{medal} **{user.Name}** - {user.Score} 🏆\n";
            }
            
            await _botClient.SendMessage(chatId,
                message,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling leaderboard for {ChatId}", chatId);
            await _botClient.SendMessage(chatId,
                "Хатогӣ рух дод.",
                cancellationToken: ct);
        }
    }
    
    private async Task HandleStatisticsAsync(long chatId, IMediator mediator, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new Application.Features.Admin.Queries.GetStatistics.GetStatisticsQuery(), ct);
            
            var message = $"📊 **Статистика**\n\n" +
                         $"👥 Ҳамагӣ корбарон: {result.TotalUsers}\n" +
                         $"✅ Фаъол имрӯз: {result.ActiveUsersToday}\n" +
                         $"📚 Ҳамагӣ саволҳо: {result.TotalQuestions}\n" +
                         $"✏️ Тестҳои ҳалшуда: {result.TotalTestsSolved}\n" +
                         $"✔️ Ҷавобҳои дуруст: {result.TotalCorrectAnswers}\n" +
                         $"📖 Фанҳо: {result.TotalSubjects}";
            
            await _botClient.SendMessage(chatId,
                message,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling statistics for {ChatId}", chatId);
            await _botClient.SendMessage(chatId,
                "Хатогӣ рух дод.",
                cancellationToken: ct);
        }
    }
    
    private async Task HandleBroadcastPromptAsync(long chatId, IMediator mediator, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
            
            var userState = await unitOfWork.UserStates.GetByChatIdAsync(chatId, ct) 
                           ?? new Domain.Entities.UserState { ChatId = chatId };
            
            userState.IsPendingBroadcast = true;
            
            if (userState.Id == 0)
                await unitOfWork.UserStates.AddAsync(userState, ct);
            else
                unitOfWork.UserStates.Update(userState);
                
            await unitOfWork.SaveChangesAsync(ct);
            
            await _botClient.SendMessage(chatId,
                "📢 Лутфан паёмро барои ҳамаи корбарон нависед:",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting broadcast mode for {ChatId}", chatId);
        }
    }
    
    private async Task ShowSubjectSelectionAsync(long chatId, IMediator mediator, CancellationToken ct)
    {
        var subjects = await mediator.Send(new Application.Features.Subjects.Queries.GetAllSubjects.GetAllSubjectsQuery(), ct);
        
        if (subjects.Count == 0)
        {
            await _botClient.SendMessage(chatId, 
                "Дар айни замон фанҳо дастрас нестанд.", cancellationToken: ct);
            return;
        }
        
        var keyboard = new ReplyKeyboardMarkup(
            subjects.Select(s => new KeyboardButton[] 
            { 
                new($"📚 {s.Name}") 
            }).Concat([["⬅️ Бозгашт"]])
        )
        {
            ResizeKeyboard = true
        };
        
        await _botClient.SendMessage(chatId, 
            "Лутфан, фанро интихоб кунед:", 
            replyMarkup: keyboard, 
            cancellationToken: ct);
    }
    
    private async Task HandleBroadcastMessageAsync(long chatId, string message, IMediator mediator, CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(new Application.Features.Admin.Commands.BroadcastMessage.BroadcastMessageCommand
            {
                AdminChatId = chatId,
                Message = message
            }, ct);
            
            if (!result.Success)
            {
                await _botClient.SendMessage(chatId, result.Message, cancellationToken: ct);
                return;
            }
            
           
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
            var users = await unitOfWork.Users.GetAllAsync(ct);
            
            var successCount = 0;
            var failureCount = 0;
            
            foreach (var user in users)
            {
                try
                {
                    await _botClient.SendMessage(user.ChatId, message, cancellationToken: ct);
                    successCount++;
                    await Task.Delay(50, ct); // Rate limiting
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send broadcast to {ChatId}", user.ChatId);
                    failureCount++;
                }
            }
            
            // Clear state
            var userState = await unitOfWork.UserStates.GetByChatIdAsync(chatId, ct);
            if (userState != null)
            {
                userState.IsPendingBroadcast = false;
                unitOfWork.UserStates.Update(userState);
                await unitOfWork.SaveChangesAsync(ct);
            }
            
            await _botClient.SendMessage(chatId,
                $"✅ Паём фиристода шуд!\n\n" +
                $"📊 Ҳамагӣ: {users.Count}\n" +
                $"✅ Муваффақ: {successCount}\n" +
                $"❌ Хатогӣ: {failureCount}",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting message from {ChatId}", chatId);
            await _botClient.SendMessage(chatId,
                "Хатогӣ ҳангоми фиристодани паём.",
                cancellationToken: ct);
        }
    }
    
    private async Task HandleDuelRequestAsync(long chatId, IMediator mediator, CancellationToken ct)
    {
        try
        {
            var topUsers = await mediator.Send(new Application.Features.Users.Queries.GetTopUsers.GetTopUsersQuery
            {
                Count = 10
            }, ct);
            
            if (topUsers.Count == 0)
            {
                await _botClient.SendMessage(chatId,
                    "Ҳоло ҳеҷ корбаре барои дуэл мавҷуд нест.",
                    cancellationToken: ct);
                return;
            }
            
            var keyboard = new InlineKeyboardMarkup(
                topUsers.Select(u => new[]
                {
                    InlineKeyboardButton.WithCallbackData($"{u.Name} - {u.Score} ⭐", $"duel_challenge_{u.ChatId}")
                })
            );
            
            await _botClient.SendMessage(chatId,
                "⚔️ **Дуэл**\n\nҲарифро интихоб кунед:",
                replyMarkup: keyboard,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling duel request for {ChatId}", chatId);
        }
    }
    
    private async Task HandleDuelCallbackAsync(long chatId, string data, IMediator mediator, CancellationToken ct)
    {
        try
        {
            var parts = data.Split('_');
            
            if (parts[1] == "create" && parts.Length == 3)
            {
                var subjectId = int.Parse(parts[2]);
                
                var result = await mediator.Send(new Application.Features.Duels.Commands.CreateDuel.CreateDuelCommand
                {
                    ChallengerChatId = chatId,
                    SubjectId = subjectId
                }, ct);
                
                if (result.Success)
                {
                    var me = await _botClient.GetMe(ct);
                    var duelLink = $"https://t.me/{me.Username}?start=duel_{result.DuelCode}";
                    
                    await _botClient.SendMessage(chatId,
                        $"⚔️ **Даъвати дуэл сохта шуд!**\n\n" +
                        $"Ссылкаро ба дӯстатон фиристед:\n\n" +
                        $"`{duelLink}`\n\n" +
                        $"Вақте онҳо клик кунанд, дуэл оғоз мешавад!",
                        parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                        cancellationToken: ct);
                }
                else
                {
                    await _botClient.SendMessage(chatId, result.Message, cancellationToken: ct);
                }
            }

            else if (parts[1] == "accept" && parts.Length == 3)
            {
                var duelId = int.Parse(parts[2]);
                var result = await mediator.Send(new Application.Features.Duels.Commands.AcceptDuel.AcceptDuelCommand
                {
                    DuelId = duelId,
                    OpponentChatId = chatId
                }, ct);
                
                await _botClient.SendMessage(chatId, result.Message, cancellationToken: ct);
            }
            else if (parts[1] == "reject")
            {
                await _botClient.SendMessage(chatId,
                    "Шумо даъватро рад кардед.",
                    cancellationToken: ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling duel callback for {ChatId}: {Data}", chatId, data);
        }
    }
    
    private async Task HandleQuestionImportRequestAsync(long chatId, IMediator mediator, CancellationToken ct)
    {
        try
        {
            var subjects = await mediator.Send(new Application.Features.Subjects.Queries.GetAllSubjects.GetAllSubjectsQuery(), ct);
            
            if (subjects.Count == 0)
            {
                await _botClient.SendMessage(chatId,
                    "Ҳоло ҳеҷ фане дар система нест.",
                    cancellationToken: ct);
                return;
            }
            
            var keyboard = new InlineKeyboardMarkup(
                subjects.Select(s => new[]
                {
                    InlineKeyboardButton.WithCallbackData(s.Name, $"import_subject_{s.Id}")
                })
            );
            
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
            var userState = await unitOfWork.UserStates.GetByChatIdAsync(chatId, ct);
            
            if (userState == null)
            {
                userState = new Domain.Entities.UserState { ChatId = chatId };
                await unitOfWork.UserStates.AddAsync(userState, ct);
                await unitOfWork.SaveChangesAsync(ct); 
            }
            
            userState.QuestionImportStep = Domain.Entities.QuestionImportStep.SelectingSubject;
            unitOfWork.UserStates.Update(userState);
            await unitOfWork.SaveChangesAsync(ct);
            
            await _botClient.SendMessage(chatId,
                "📥 **Дохил кардани саволҳо**\n\nФанро интихоб кунед:",
                replyMarkup: keyboard,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling question import request for {ChatId}", chatId);
        }
    }
    
    private async Task HandleQuestionImportFlowAsync(long chatId, Telegram.Bot.Types.Message message, IMediator mediator, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
            var userState = await unitOfWork.UserStates.GetByChatIdAsync(chatId, ct);
            
            if (userState?.QuestionImportStep != Domain.Entities.QuestionImportStep.UploadingFile)
                return;
            
            if (message.Document == null)
            {
                await _botClient.SendMessage(chatId,
                    "❌ Лутфан, файл ирсол кунед (.pdf, .docx, .doc)",
                    cancellationToken: ct);
                return;
            }
            
            var file = message.Document;
            var extension = Path.GetExtension(file.FileName ?? "").ToLower();
            
            if (extension != ".pdf" && extension != ".docx" && extension != ".doc")
            {
                await _botClient.SendMessage(chatId,
                    "❌ Формати дастгарӣ нашуда. Танҳо .pdf, .docx, .doc",
                    cancellationToken: ct);
                return;
            }
            
            var processingMsg = await _botClient.SendMessage(chatId,
                "⏳ Дар ҳоли коркард... Лутфан интизор шавед.",
                cancellationToken: ct);
            
            // Download file
            var fileInfo = await _botClient.GetFile(file.FileId, ct);
            using var fileStream = new MemoryStream();
            await _botClient.DownloadFile(fileInfo.FilePath!, fileStream, ct);
            var fileContent = fileStream.ToArray();
            
            // Import questions
            var result = await mediator.Send(new Application.Features.Questions.Commands.ImportQuestions.ImportQuestionsCommand
            {
                SubjectId = userState.ImportSubjectId!.Value,
                FileContent = fileContent,
                FileName = file.FileName ?? "file",
                FileExtension = extension
            }, ct);
            
            // Clear state
            userState.QuestionImportStep = null;
            userState.ImportSubjectId = null;
            unitOfWork.UserStates.Update(userState);
            await unitOfWork.SaveChangesAsync(ct);
            
            // Show result
            var resultMessage = $"📊 **Натиҷа:**\n\n" +
                                $"✅ Саволҳои нав: {result.SuccessfullyAdded}\n" +
                                $"🔄 Такрорӣ: {result.Duplicates}\n" +
                                $"❌ Хатогӣ: {result.Errors}\n\n" +
                                $"📝 Ҷамъ: {result.TotalParsed} савол парс шуд";
            
            if (result.ErrorMessages.Any())
            {
                resultMessage += $"\n\n⚠️ Хатогиҳо:\n{string.Join("\n", result.ErrorMessages.Take(5))}";
            }
            
            await _botClient.EditMessageText(chatId, processingMsg.MessageId,
                resultMessage,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling question import flow for {ChatId}", chatId);
            await _botClient.SendMessage(chatId,
                "❌ Хатогӣ ҳангоми коркарди файл.",
                cancellationToken: ct);
        }
    }
    
    private async Task HandleImportSubjectCallbackAsync(long chatId, string data, IMediator mediator, CancellationToken ct)
    {
        try
        {
            var parts = data.Split('_');
            if (parts.Length < 3) return;
            
            var subjectId = int.Parse(parts[2]);
            
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
            var userState = await unitOfWork.UserStates.GetByChatIdAsync(chatId, ct);
            
            if (userState == null) return;
            
            userState.QuestionImportStep = Domain.Entities.QuestionImportStep.UploadingFile;
            userState.ImportSubjectId = subjectId;
            unitOfWork.UserStates.Update(userState);
            await unitOfWork.SaveChangesAsync(ct);
            
            await _botClient.SendMessage(chatId,
                "📄 Файли саволҳоро ирсол кунед (.pdf, .docx, .doc):",
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling import subject callback for {ChatId}: {Data}", chatId, data);
        }
    }
    
    private async Task HandleStartTestAsync(long chatId, IMediator mediator, CancellationToken ct)
{
    try
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
        var userState = await unitOfWork.UserStates.GetByChatIdAsync(chatId, ct);
        
        if (userState?.SelectedSubject == null)
        {
            await _botClient.SendMessage(chatId,
                "Лутфан аввал фанро интихоб кунед!",
                cancellationToken: ct);
            return;
        }
        
        var question = await mediator.Send(new Application.Features.Questions.Queries.GetRandomQuestion.GetRandomQuestionQuery
        {
            SubjectId = userState.SelectedSubject.Id
        }, ct);
        
        if (question == null)
        {
            await _botClient.SendMessage(chatId,
                "Саволҳо барои ин фан дастрас нестанд.",
                cancellationToken: ct);
            return;
        }
        
       
        var subject = await unitOfWork.Subjects.GetByIdAsync(userState.SelectedSubject.Id, ct);
        
        var timerText = "";
        if (subject?.HasTimer == true && subject.TimerSeconds.HasValue)
        {
            int minutes = subject.TimerSeconds.Value / 60;
            int seconds = subject.TimerSeconds.Value % 60;
            timerText = $" ⏱ {minutes:D2}:{seconds:D2}";
        }
        
        var keyboard = new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData($"A) {question.OptionA}", $"answer_{question.Id}_A"),
                InlineKeyboardButton.WithCallbackData($"B) {question.OptionB}", $"answer_{question.Id}_B")
            ],
            [
                InlineKeyboardButton.WithCallbackData($"C) {question.OptionC}", $"answer_{question.Id}_C"),
                InlineKeyboardButton.WithCallbackData($"D) {question.OptionD}", $"answer_{question.Id}_D")
            ]
        ]);
        
        var messageText = $"📚 **Фан: {question.SubjectName}**{timerText}\n\n" +
                         $"❓ {question.Text}\n";
        
        await _botClient.SendMessage(chatId,
            messageText,
            replyMarkup: keyboard,
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            cancellationToken: ct);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error starting test for {ChatId}", chatId);
        await _botClient.SendMessage(chatId,
            "Хатогӣ рух дод.",
            cancellationToken: ct);
    }
}
    
    private async Task HandleAnswerCallbackAsync(long chatId, int messageId, string data, IMediator mediator, CancellationToken ct)
{
    try
    {
        var parts = data.Split('_');
        if (parts.Length != 3)
            return;
            
        var questionId = int.Parse(parts[1]);
        var selectedAnswer = parts[2];
        
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
        var question = await unitOfWork.Questions.GetByIdAsync(questionId, ct);
        
        if (question == null)
            return;
        
        var result = await mediator.Send(new Application.Features.Tests.Commands.HandleAnswer.HandleAnswerCommand
        {
            ChatId = chatId,
            QuestionId = questionId,
            SelectedAnswer = selectedAnswer
        }, ct);
        
        var correctAnswer = result.CorrectAnswer;
        var buttons = new[]
        {
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"A) {question.Option.OptionA}" + (correctAnswer == "A" ? " ✅" : selectedAnswer == "A" ? " ❌" : ""),
                    $"answered_A"),
                InlineKeyboardButton.WithCallbackData(
                    $"B) {question.Option.OptionB}" + (correctAnswer == "B" ? " ✅" : selectedAnswer == "B" ? " ❌" : ""),
                    $"answered_B")
            },
            new[]
            {
                InlineKeyboardButton.WithCallbackData(
                    $"C) {question.Option.OptionC}" + (correctAnswer == "C" ? " ✅" : selectedAnswer == "C" ? " ❌" : ""),
                    $"answered_C"),
                InlineKeyboardButton.WithCallbackData(
                    $"D) {question.Option.OptionD}" + (correctAnswer == "D" ? " ✅" : selectedAnswer == "D" ? " ❌" : ""),
                    $"answered_D")
            }
        };
        
        var keyboard = new InlineKeyboardMarkup(buttons);
        

        var feedback = result.IsCorrect
            ? $"\n\n✅ **Дуруст!**\n🏆 Холҳо: {result.CurrentScore}\n📊 Ҷавобҳо: {result.QuestionsAnswered}"
            : $"\n\n❌ **Нодуруст!**\n📝 Ҷавоби дуруст: {result.CorrectAnswer}\n🏆 Холҳо: {result.CurrentScore}\n📊 Ҷавобҳо: {result.QuestionsAnswered}";
        
        try
        {
            await _botClient.EditMessageReplyMarkup(
                chatId: chatId,
                messageId: messageId,
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
        catch
        {
            // 
        }
        
        
        
        await _botClient.SendMessage(chatId,
            feedback,
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            cancellationToken: ct);
        
        if (!result.TestCompleted)
        {
            await Task.Delay(2000, ct);
            await HandleStartTestAsync(chatId, mediator, ct);
        }
        else
        {
            await _botClient.SendMessage(chatId,
                $"🎉 **Тест тамом шуд!**\n\n🏆 Холҳои ниҳоӣ: {result.CurrentScore}",
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: ct);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error handling answer callback for {ChatId}", chatId);
    }
}
    
    private async Task HandleLibraryAsync(long chatId, IMediator mediator, CancellationToken ct)
    {
        var query = new Application.Features.Library.Queries.GetAllBooks.GetAllBooksQuery();
        var books = await mediator.Send(query, ct);
        
        if (books.Count == 0)
        {
            await _botClient.SendMessage(chatId, 
                "📚 Китобхона холӣ аст. Ҳеҷ китобе мавҷуд нест.",
                cancellationToken: ct);
            return;
        }
        foreach (var book in books.Take(10))
        {
            var message = $"📖 {book.Title}\n" +
                         $"📝 {book.Description}\n" +
                         $"📅 Сол: {book.PublicationYear}\n" +
                         $"🏷 Категория: {book.CategoryName}";
            
            var inlineKeyboard = new InlineKeyboardMarkup(
            [
                [
                    InlineKeyboardButton.WithCallbackData(
                            "⬇️ Зеркашӣ", 
                            $"download_book_{book.Id}")
                ]
            ]);
            
            await _botClient.SendMessage(chatId, message,
                replyMarkup: inlineKeyboard,
                cancellationToken: ct);
            
            await Task.Delay(100, ct);
        }
        
        if (books.Count > 10)
        {
            await _botClient.SendMessage(chatId,
                $"📚 Ва {books.Count - 10} китоби дигар мавҷуд аст.",
                cancellationToken: ct);
        }
    }
    
    private async Task HandleBookDownloadAsync(long chatId, string text, IMediator mediator, CancellationToken ct)
    {
        var bookIdStr = text.Replace("/book", "").Trim();
        
        if (!int.TryParse(bookIdStr, out var bookId))
        {
            await _botClient.SendMessage(chatId,
                "Команда нодуруст. Истифода: /book1, /book2, ...",
                cancellationToken: ct);
            return;
        }
        
       
        var loadingMsg = await _botClient.SendMessage(chatId,
            "⏬ Китоб тайёр мешавад...",
            cancellationToken: ct);
        
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
            
            var book = await unitOfWork.Books.GetByIdAsync(bookId, ct);
            
            if (book == null || !book.IsActive)
            {
                await _botClient.EditMessageText(chatId, loadingMsg.MessageId,
                    "❌ Китоб ёфт нашуд.",
                    cancellationToken: ct);
                return;
            }
            
            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), book.FilePath);
            
            if (!File.Exists(fullPath))
            {
                await _botClient.EditMessageText(chatId, loadingMsg.MessageId,
                    "❌ Файли китоб ёфт нашуд дар сервер.",
                    cancellationToken: ct);
                _logger.LogError("Book file not found: {FilePath}", fullPath);
                return;
            }
            
            await _botClient.EditMessageText(chatId, loadingMsg.MessageId,
                "📤 Китоб фиристода мешавад...",
                cancellationToken: ct);
            
            
            await using var fileStream = File.OpenRead(fullPath);
            await _botClient.SendDocument(chatId,
                new InputFileStream(fileStream, book.FileName),
                caption: $"📖 {book.Title}\n📝 {book.Description}\n📅 Сол: {book.Year}",
                cancellationToken: ct);
            
        
            book.IncrementDownloadCount();
            unitOfWork.Books.Update(book);
            await unitOfWork.SaveChangesAsync(ct);
            
            await _botClient.DeleteMessage(chatId, loadingMsg.MessageId, ct);
            
            _logger.LogInformation("Book {BookId} ({Title}) downloaded by user {ChatId}", 
                bookId, book.Title, chatId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading book {BookId}", bookId);

            try
            {
                await _botClient.EditMessageText(chatId, loadingMsg.MessageId,
                    "❌ Хатогӣ ҳангоми фиристодани китоб. Лутфан баъдтар кӯшиш кунед.",
                    cancellationToken: ct);
            }
            catch
            {
                //
            }
        }
    }
    
    private async Task HandleSetAdminCommandAsync(long chatId, string text, IMediator mediator, CancellationToken ct)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        if (parts.Length < 2)
        {
            await _botClient.SendMessage(chatId, 
                "Истифода: /setadmin @username ё /setadmin 992711116888", 
                cancellationToken: ct);
            return;
        }
        
        var target = parts[1].TrimStart('@'); 
        
        var command = new Application.Features.Admin.Commands.SetAdmin.SetAdminCommand
        {
            AdminChatId = chatId,
            TargetUsername = target.StartsWith("992") ? null : target,
            TargetPhoneNumber = target.StartsWith("992") ? target : null,
            MakeAdmin = true
        };
        
        var result = await mediator.Send(command, ct);
        
        await _botClient.SendMessage(chatId, result.Message, cancellationToken: ct);
    }
    
    
    private async Task HandleSubjectSelectionAsync(long chatId, string text, IMediator mediator, CancellationToken ct)
    {
        var subjectName = text.Replace("📚 ", "").Trim();
        
        var allSubjects = await mediator.Send(
            new Application.Features.Subjects.Queries.GetAllSubjects.GetAllSubjectsQuery(), ct);
        
        var selected = allSubjects.FirstOrDefault(s => s.Name == subjectName);
        if (selected == null)
        {
            await _botClient.SendMessage(chatId, "Фан ёфт нашуд!", cancellationToken: ct);
            return;
        }
        
        var command = new Application.Features.Bot.Commands.SelectSubject.SelectSubjectCommand
        {
            ChatId = chatId,
            SubjectId = selected.Id
        };
        
        var result = await mediator.Send(command, ct);
        
        var mainKeyboard = GetMainMenuKeyboard();
        await _botClient.SendMessage(chatId, result.Message, 
            replyMarkup: mainKeyboard, cancellationToken: ct);
    }
    
    private async Task<Domain.Entities.RegistrationSession?> GetRegistrationSessionAsync(
        long chatId, 
        IMediator mediator, 
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
        return await unitOfWork.RegistrationSessions.GetActiveByChatIdAsync(chatId, ct);
    }
    
    private async Task HandleRegistrationFlowAsync(
        long chatId, 
        string text,
        Domain.Entities.RegistrationSession session,
        IMediator mediator,
        CancellationToken ct)
    {
        if (session.CurrentStep == Domain.Entities.RegistrationStep.Name)
        {
            var command = new Application.Features.Bot.Commands.HandleNameRegistration.HandleNameRegistrationCommand
            {
                ChatId = chatId,
                Name = text
            };
            
            var result = await mediator.Send(command, ct);
            await _botClient.SendMessage(chatId, result.Message, cancellationToken: ct);
        }
        else if (session.CurrentStep == Domain.Entities.RegistrationStep.City)
        {
            var command = new Application.Features.Bot.Commands.HandleCityRegistration.HandleCityRegistrationCommand
            {
                ChatId = chatId,
                City = text
            };
            
            var result = await mediator.Send(command, ct);
            
            if (result.IsCompleted)
            {
                var mainKeyboard = GetMainMenuKeyboard();
                await _botClient.SendMessage(chatId, result.Message, replyMarkup: mainKeyboard, cancellationToken: ct);
            }
            else
            {
                await _botClient.SendMessage(chatId, result.Message, cancellationToken: ct);
            }
        }
    }
    
    private async Task<ReplyKeyboardMarkup> GetMainMenuKeyboardAsync(long chatId, IMediator mediator, CancellationToken ct)
    {
        var buttons = new List<KeyboardButton[]>
        {
            new KeyboardButton[] { "📚 Интихоби фан", "🎯 Оғози тест" },
            new KeyboardButton[] { "👤 Профил", "🏆 Беҳтаринҳо" },
            new KeyboardButton[] { "📚 Китобхона", "👥 Даъвати дӯстон" }
        };
        
        var user = await mediator.Send(new Application.Features.Users.Queries.GetUserProfile.GetUserProfileQuery
        {
            ChatId = chatId
        }, ct);
        
        if (user != null)
        {
            // Get full user to check admin status
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
            var fullUser = await unitOfWork.Users.GetByChatIdAsync(chatId, ct);
            
            if (fullUser?.IsAdmin == true)
            {
                buttons.Add(["📊 Статистика", "📢 Паём фиристодан"]);
                buttons.Add(["📥 Дохил кардани саволҳо", "📤 Боргузории китоб"]);
            }
        }
        
        return new ReplyKeyboardMarkup(buttons)
        {
            ResizeKeyboard = true
        };
    }
    
    private async Task HandleDuelInvitationAsync(long chatId, string duelCode, IMediator mediator, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
            
            var duel = await unitOfWork.Duels.GetByCodeAsync(duelCode, ct);
            
            if (duel == null || duel.Status != Domain.Entities.DuelStatus.Pending)
            {
                await _botClient.SendMessage(chatId, "❌ Дуэл ёфт нашуд ё аллакай тамом шуд.", cancellationToken: ct);
                return;
            }
            
            var opponent = await unitOfWork.Users.GetByChatIdAsync(chatId, ct);
            
            if (opponent.Id == duel.ChallengerId)
            {
                await _botClient.SendMessage(chatId, "❌ Шумо наметавонед бо худатон дуэл кунед!", cancellationToken: ct);
                return;
            }
            
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData("✅ Қабул кардан", $"duel_accept_{duel.Id}"),
                    InlineKeyboardButton.WithCallbackData("❌ Рад кардан", $"duel_reject_{duel.Id}")
                }
            });
            
            await _botClient.SendMessage(chatId,
                $"⚔️ **Даъват ба дуэл!**\n\n" +
                $"{duel.Challenger.Name} шуморо ба дуэл даъват кард!\n" +
                $"📚 Фан: {duel.Subject.Name}",
                replyMarkup: keyboard,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling duel invitation for {ChatId}: {DuelCode}", chatId, duelCode);
        }
    }
    
    private ReplyKeyboardMarkup GetMainMenuKeyboard(Domain.Entities.User? user = null)
    {
        var buttons = new List<KeyboardButton[]>
        {
            new KeyboardButton[] { "📚 Интихоби фан", "🎯 Оғози тест" },
            new KeyboardButton[] { "👤 Профил", "🏆 Беҳтаринҳо" },
            new KeyboardButton[] { "⚔️ Дуэл", "📊 Натиҷаҳо" },
            new KeyboardButton[] { "📚 Китобхона", "👥 Даъвати дӯстон" }
        };
        
        if (user?.IsAdmin == true)
        {
            buttons.Add(new KeyboardButton[] { "📊 Статистика", "📢 Паём фиристодан" });
            buttons.Add(new KeyboardButton[] { "📥 Дохил кардани саволҳо", "📤 Боргузории китоб" });
        }
        else
        {
            buttons.Add(new KeyboardButton[] { "📤 Боргузории китоб" });
        }
        
        return new ReplyKeyboardMarkup(buttons)
        {
            ResizeKeyboard = true
        };
    }

    private Task HandleErrorAsync(
        ITelegramBotClient botClient,
        Exception exception,
        CancellationToken cancellationToken)
    {
        _logger.LogError(exception, "Error in polling");
        return Task.CompletedTask;
    }

    
    private async Task<Domain.Entities.UserState?> GetUserStateAsync(
        long chatId,
        IMediator mediator,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
        return await unitOfWork.UserStates.GetOrCreateAsync(chatId, ct);
    }
    
    private async Task StartBookUploadAsync(long chatId, IMediator mediator, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
        var user = await unitOfWork.Users.GetByChatIdAsync(chatId, ct);
        
        if (user == null || !user.IsAdmin)
        {
            await _botClient.SendMessage(chatId, 
                "Шумо ҳуқуқи боргузорӣ надоред.",
                cancellationToken: ct);
            return;
        }
        
        var userState = await unitOfWork.UserStates.GetOrCreateAsync(chatId, ct);
        userState.ClearBookUpload();
        userState.BookUploadStep = Domain.Entities.BookUploadStep.Title;
        
        unitOfWork.UserStates.Update(userState);
        await unitOfWork.SaveChangesAsync(ct);
        
        await _botClient.SendMessage(chatId, 
            "📤 *Боргузории китоб*\n\nНоми китобро ворид кунед:",
            parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
            cancellationToken: ct);
    }
    
    private async Task HandleBookUploadFlowAsync(
        long chatId,
        string text,
        Domain.Entities.UserState userState,
        IMediator mediator,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
        
        switch (userState.BookUploadStep)
        {
            case Domain.Entities.BookUploadStep.Title:
                userState.BookTitle = text;
                userState.BookUploadStep = Domain.Entities.BookUploadStep.Description;
                unitOfWork.UserStates.Update(userState);
                await unitOfWork.SaveChangesAsync(ct);
                
                await _botClient.SendMessage(chatId, 
                    "Тавсифи китобро ворид кунед:",
                    cancellationToken: ct);
                break;
            
            case Domain.Entities.BookUploadStep.Description:
                userState.BookDescription = text;
                userState.BookUploadStep = Domain.Entities.BookUploadStep.Year;
                unitOfWork.UserStates.Update(userState);
                await unitOfWork.SaveChangesAsync(ct);
                
                await _botClient.SendMessage(chatId, 
                    "Соли нашри китобро ворид кунед:",
                    cancellationToken: ct);
                break;
            
            case Domain.Entities.BookUploadStep.Year:
                if (int.TryParse(text, out var year))
                {
                    userState.BookYear = year;
                    userState.BookUploadStep = Domain.Entities.BookUploadStep.Category;
                    unitOfWork.UserStates.Update(userState);
                    await unitOfWork.SaveChangesAsync(ct);
                    
                    await _botClient.SendMessage(chatId, 
                        "Категорияи китобро ворид кунед (масалан: Биология, Адабиёт, Таърих):",
                        cancellationToken: ct);
                }
                else
                {
                    await _botClient.SendMessage(chatId, 
                        "Рақами нодуруст. Лутфан соли нашрро бо рақам ворид кунед:",
                        cancellationToken: ct);
                }
                break;
            
            case Domain.Entities.BookUploadStep.Category:
                userState.BookCategory = text;
                userState.BookUploadStep = Domain.Entities.BookUploadStep.File;
                unitOfWork.UserStates.Update(userState);
                await unitOfWork.SaveChangesAsync(ct);
                
                await _botClient.SendMessage(chatId, 
                    "Китобро ҳамчун файл фиристед (PDF, EPUB, ғайра):",
                    cancellationToken: ct);
                break;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Telegram Bot Service stopping");
        await base.StopAsync(cancellationToken);
    }
    private async Task HandleDocumentAsync(Message message, IMediator mediator, CancellationToken ct)
    {
        var chatId = message.Chat.Id;
        
        var userState = await GetUserStateAsync(chatId, mediator, ct);
        
        
        if (userState?.QuestionImportStep == Domain.Entities.QuestionImportStep.UploadingFile)
        {
            await HandleQuestionImportFlowAsync(chatId, message, mediator, ct);
            return;
        }
        
        // Check if user is uploading a book
        if (userState?.BookUploadStep == Domain.Entities.BookUploadStep.File)
        {
            var loadingMessage = await _botClient.SendMessage(chatId,
                "⏳ Китоб бор мешавад...\nЛутфан интизор шавед.",
                cancellationToken: ct);
            
            try
            {
       
    
                var document = message.Document!;
                var fileId = document.FileId;
                var fileName = document.FileName ?? $"book_{DateTime.UtcNow:yyyyMMdd_HHmmss}.pdf";
                
                var file = await _botClient.GetFile(fileId, ct);
                var filePath = $"uploads/books/{Guid.NewGuid()}_{fileName}";
                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), filePath);
                
                var directory = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                await using var fileStream = File.Create(fullPath);
                await _botClient.DownloadFile(file.FilePath!, fileStream, ct);
                
                var command = new Application.Features.Library.Commands.UploadBook.UploadBookCommand
                {
                    AdminChatId = chatId,
                    Title = userState.BookTitle ?? "Номаълум",
                    Description = userState.BookDescription ?? "",
                    PublicationYear = userState.BookYear ?? DateTime.UtcNow.Year,
                    Category = userState.BookCategory ?? "Умумӣ",
                    FileName = fileName,
                    FilePath = filePath
                };
                
                using var scope = _scopeFactory.CreateScope();
                var result = await mediator.Send(command, ct);
                
                userState.ClearBookUpload();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
                unitOfWork.UserStates.Update(userState);
                await unitOfWork.SaveChangesAsync(ct);
                
                await _botClient.DeleteMessage(chatId, loadingMessage.MessageId, ct);
                
                await _botClient.SendMessage(chatId,
                    $"✅ {result.Message}\n\n" +
                    $"📖 Ном: {command.Title}\n" +
                    $"📝 Тавсиф: {command.Description}\n" +
                    $"📅 Сол: {command.PublicationYear}\n" +
                    $"📄 Файл: {fileName}",
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading book file");

                try
                {
                    await _botClient.DeleteMessage(chatId, loadingMessage.MessageId, ct);
                }
                catch
                {
                    //
                }
                
                await _botClient.SendMessage(chatId,
                    "❌ Хатогӣ ҳангоми боргузорӣ рух дод. Лутфан дубора кӯшиш кунед.",
                    cancellationToken: ct);
            }
        }
        else
        {
            await _botClient.SendMessage(chatId,
                "Лутфан аввал боргузории китобро оғоз кунед: 📤 Боргузории китоб",
                cancellationToken: ct);
        }
    }
    
    private async Task HandleBackButtonAsync(long chatId, IMediator mediator, CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
            
            var userState = await unitOfWork.UserStates.GetByChatIdAsync(chatId, ct);
            if (userState != null)
            {
                userState.BookUploadStep = null;
                userState.BookTitle = null;
                userState.BookDescription = null;
                userState.BookYear = null;
                userState.BookCategory = null;
                userState.QuestionImportStep = null;
                userState.ImportSubjectId = null;
                userState.IsPendingBroadcast = false;
                userState.IsPendingNameChange = false;
                
                unitOfWork.UserStates.Update(userState);
                await unitOfWork.SaveChangesAsync(ct);
            }
            
            var user = await unitOfWork.Users.GetByChatIdAsync(chatId, ct);
            var keyboard = GetMainMenuKeyboard(user);
            
            await _botClient.SendMessage(chatId,
                "🏠 Менюи асосӣ",
                replyMarkup: keyboard,
                cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling back button for {ChatId}", chatId);
        }
    }
}
