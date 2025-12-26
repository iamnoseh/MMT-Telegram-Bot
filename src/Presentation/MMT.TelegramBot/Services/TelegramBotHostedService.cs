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
        
        if (data?.StartsWith("download_book_") == true)
        {
            var bookIdStr = data.Replace("download_book_", "");
            if (int.TryParse(bookIdStr, out var bookId))
            {
                await HandleBookDownloadAsync(chatId, $"/book{bookId}", mediator, ct);
            }
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
        if (text.StartsWith("/start ref_"))
        {
            referralCode = text.Replace("/start ref_", "").Trim();
            _logger.LogInformation("Referral code detected: {Code} for user {ChatId}", referralCode, chatId);
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
            var mainKeyboard = GetMainMenuKeyboard();
            await _botClient.SendMessage(chatId, result.Message, replyMarkup: mainKeyboard, cancellationToken: ct);
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
        
        // Check if  admin is sending broadcast message
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
                         $"📊 Ранг: #{result.Rank}\n" +
                         $"📱 Телефон: {result.PhoneNumber}";
            
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
            }).Concat(new[] { new KeyboardButton[] { "⬅️ Бозгашт" } })
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
            
            // Clear pending broadcast state
            using var scope = _scopeFactory.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<Application.Common.Interfaces.Repositories.IUnitOfWork>();
            var userState = await unitOfWork.UserStates.GetByChatIdAsync(chatId, ct);
            
            if (userState != null)
            {
                userState.IsPendingBroadcast = false;
                unitOfWork.UserStates.Update(userState);
                await unitOfWork.SaveChangesAsync(ct);
            }
            
            await _botClient.SendMessage(chatId,
                $"✅ Паём фиристода шуд!\n\n" +
                $"📊 Ҳамагӣ: {result.TotalUsers}\n" +
                $"✅ Муваффақ: {result.SuccessCount}\n" +
                $"❌ Хатогӣ: {result.FailureCount}",
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
            
            var keyboard = new InlineKeyboardMarkup(new[]
            {
                new[]
                {
                    InlineKeyboardButton.WithCallbackData($"А) {question.OptionA}", $"answer_{question.Id}_A")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData($"Б) {question.OptionB}", $"answer_{question.Id}_B")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData($"В) {question.OptionC}", $"answer_{question.Id}_C")
                },
                new[]
                {
                    InlineKeyboardButton.WithCallbackData($"Г) {question.OptionD}", $"answer_{question.Id}_D")
                }
            });
            
            await _botClient.SendMessage(chatId,
                $"❓ **Савол** ({question.SubjectName})\n\n{question.Text}",
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
    
    private async Task HandleAnswerCallbackAsync(long chatId, string data, IMediator mediator, CancellationToken ct)
    {
        try
        {
            // Parse answer data: answer_{questionId}_{selectedAnswer}
            var parts = data.Split('_');
            if (parts.Length != 3)
                return;
                
            var questionId = int.Parse(parts[1]);
            var selectedAnswer = parts[2];
            
            // Submit answer
            var result = await mediator.Send(new Application.Features.Tests.Commands.HandleAnswer.HandleAnswerCommand
            {
                ChatId = chatId,
                QuestionId = questionId,
                SelectedAnswer = selectedAnswer
            }, ct);
            
            // Show result
            var emoji = result.IsCorrect ? "✅" : "❌";
            var message = result.IsCorrect
                ? $"{emoji} **Дуруст!**\n\n🏆 Холҳо: {result.CurrentScore}\n📊 Ҷавобҳо: {result.QuestionsAnswered}"
                : $"{emoji} **Нодуруст!**\n\n📝 Ҷавоби дуруст: {result.CorrectAnswer}\n🏆 Холҳо: {result.CurrentScore}\n📊 Ҷавобҳо: {result.QuestionsAnswered}";
            
            await _botClient.SendMessage(chatId,
                message,
                parseMode: Telegram.Bot.Types.Enums.ParseMode.Markdown,
                cancellationToken: ct);
            
            // Show next question after 2 seconds
            if (!result.TestCompleted)
            {
                await Task.Delay(1000, ct);
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
                new[]
                {
                    new[]
                    {
                        InlineKeyboardButton.WithCallbackData(
                            "⬇️ Зеркашӣ", 
                            $"download_book_{book.Id}")
                    }
                });
            
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
        
        // Check if user is admin
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
                buttons.Add(new KeyboardButton[] { "📊 Статистика", "📢 Паём фиристодан" });
                buttons.Add(new KeyboardButton[] { "📤 Боргузории китоб" });
            }
        }
        
        return new ReplyKeyboardMarkup(buttons)
        {
            ResizeKeyboard = true
        };
    }
    
    private ReplyKeyboardMarkup GetMainMenuKeyboard()
    {
        return new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "📚 Интихоби фан", "🎯 Оғози тест" },
            new KeyboardButton[] { "👤 Профил", "🏆 Беҳтаринҳо" },
            new KeyboardButton[] { "📚 Китобхона", "👥 Даъвати дӯстон" },
            new KeyboardButton[] { "📤 Боргузории китоб" } 
        })
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
}
