using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TradingBotEngine.Data;

namespace TradingBotEngine.Services
{
    /// <summary>
    /// Phase 0 safety worker. It discovers eligible users and tracked symbols but does not
    /// submit orders until Phase 1 has supplied real market data, risk sizing, exchange
    /// rule validation and reconciliation.
    /// </summary>
    public class AutoTradeService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AutoTradeService> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(10);

        public AutoTradeService(IServiceProvider serviceProvider, ILogger<AutoTradeService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("AutoTradeService started in safety mode.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await InspectAutoTradeConfigurationAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Auto-trade worker inspection failed.");
                }

                try
                {
                    await Task.Delay(_interval, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }

            _logger.LogInformation("AutoTradeService stopped.");
        }

        private async Task InspectAutoTradeConfigurationAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var users = await db.Users
                .AsNoTracking()
                .Where(u => u.IsActive && u.IsAutoTradeEnabled)
                .Select(u => new
                {
                    u.Id,
                    u.Email,
                    HasSubscription = u.Subscription != null &&
                                      u.Subscription.IsActive &&
                                      u.Subscription.EndDate > DateTime.UtcNow,
                    TrackedSymbols = u.TrackedSymbols.Count(s => s.IsEnabled)
                })
                .ToListAsync(cancellationToken);

            foreach (var user in users)
            {
                if (!user.HasSubscription)
                {
                    _logger.LogWarning("Auto-trade blocked for user {UserId}: subscription is inactive or expired.", user.Id);
                    continue;
                }

                if (user.TrackedSymbols == 0)
                {
                    _logger.LogWarning("Auto-trade blocked for user {UserId}: no enabled tracked symbols.", user.Id);
                    continue;
                }

                _logger.LogInformation(
                    "Auto-trade configuration ready for user {UserId} with {SymbolCount} tracked symbols; order execution remains disabled until Phase 1 risk validation is active.",
                    user.Id,
                    user.TrackedSymbols);
            }
        }
    }
}
