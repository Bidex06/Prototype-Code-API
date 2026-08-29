using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TradingBotEngine.Services
{
    public class TelegramBotService : BackgroundService
    {
        private readonly ITelegramBotClient? _botClient;
        private readonly ILogger<TelegramBotService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly bool _isEnabled;

        public TelegramBotService(
            IConfiguration configuration,
            ILogger<TelegramBotService> logger,
            IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;

            var token = configuration["Telegram:BotToken"];
            if (string.IsNullOrWhiteSpace(token) || token.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Telegram bot is disabled because no valid bot token is configured.");
                _isEnabled = false;
                return;
            }

            _botClient = new TelegramBotClient(token);
            _isEnabled = true;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_isEnabled || _botClient == null)
                return;

            _logger.LogInformation("Telegram bot service is starting.");

            var receiverOptions = new ReceiverOptions
            {
                AllowedUpdates = new[] { UpdateType.Message },
                ThrowPendingUpdates = true
            };

            var me = await _botClient.GetMeAsync(stoppingToken);
            _logger.LogInformation("Telegram bot started: @{Username}", me.Username);

            _botClient.StartReceiving(
                HandleUpdateAsync,
                HandleErrorAsync,
                receiverOptions,
                stoppingToken);

            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
        }

        private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
        {
            if (update.Message is not { } message || message.Text is not { } messageText)
                return;

            try
            {
                // Never log raw Telegram message text. It may contain credentials or other secrets.
                var commandName = messageText.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "<empty>";
                _logger.LogInformation("Received Telegram command {Command} from chat {ChatId}.", commandName, message.Chat.Id);

                using var scope = _serviceProvider.CreateScope();
                var handler = scope.ServiceProvider.GetRequiredService<TelegramCommandHandler>();
                var response = await handler.HandleCommandAsync(
                    message.Chat.Id,
                    message.Chat.Username ?? "Unknown",
                    messageText,
                    message);

                if (!string.IsNullOrWhiteSpace(response))
                    await botClient.SendTextMessageAsync(message.Chat.Id, response, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling Telegram command for chat {ChatId}.", message.Chat.Id);
                await botClient.SendTextMessageAsync(
                    message.Chat.Id,
                    "⚠️ The request could not be processed. Please use the web dashboard or try again later.",
                    cancellationToken: cancellationToken);
            }
        }

        private Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
        {
            if (exception is ApiRequestException apiException)
                _logger.LogError("Telegram API error {ErrorCode}.", apiException.ErrorCode);
            else
                _logger.LogError(exception, "Unexpected Telegram service error.");

            return Task.CompletedTask;
        }

        public async Task SendAlertAsync(long chatId, string message)
        {
            if (!_isEnabled || _botClient == null)
                return;

            try
            {
                await _botClient.SendTextMessageAsync(chatId, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send Telegram alert to chat {ChatId}.", chatId);
            }
        }
    }
}
