using MediatR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

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

    private Task HandleUpdateAsync(
        ITelegramBotClient botClient,
        Update update,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            _logger.LogDebug("Received update {UpdateId}", update.Id);

            // TODO: Implement update handling with MediatR
            // Example:
            // await mediator.Send(new HandleTelegramUpdateCommand { Update = update }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling update {UpdateId}", update.Id);
        }

        return Task.CompletedTask;
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
