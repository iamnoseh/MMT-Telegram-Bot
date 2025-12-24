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
    
    private async Task HandleMessageAsync(Message message, IMediator mediator, CancellationToken ct)
    {
        if (message.Text == null) return;
        
        var chatId = message.Chat.Id;
        var text = message.Text;
        
        _logger.LogInformation("Message from {ChatId}: {Text}", chatId, text);
        
        if (text == "/start")
        {
            var command = new Application.Features.Bot.Commands.HandleStart.HandleStartCommand
            {
                ChatId = chatId,
                Username = message.From?.Username,
                FirstName = message.From?.FirstName
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
    }
    
    private async Task HandleCallbackQueryAsync(CallbackQuery callbackQuery, IMediator mediator, CancellationToken ct)
    {
        _logger.LogInformation("Callback from {ChatId}: {Data}", callbackQuery.From.Id, callbackQuery.Data);
        
        await _botClient.AnswerCallbackQuery(callbackQuery.Id, cancellationToken: ct);
    }
    
    private ReplyKeyboardMarkup GetMainMenuKeyboard()
    {
        return new ReplyKeyboardMarkup(new[]
        {
            new KeyboardButton[] { "🎯 Оғози тест", "📊 Натиҷаҳо" },
            new KeyboardButton[] { "📚 Китобхона", "ℹ️ Маълумот" }
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Telegram Bot Service stopping");
        await base.StopAsync(cancellationToken);
    }
}
