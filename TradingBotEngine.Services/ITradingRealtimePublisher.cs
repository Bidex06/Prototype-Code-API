using TradingBotEngine.Core.Models;

namespace TradingBotEngine.Services;

/// <summary>
/// Best-effort realtime notifications for authenticated users.
/// Implementations must not become part of the trading critical path.
/// </summary>
public interface ITradingRealtimePublisher
{
    Task PublishSignalAsync(
        int userId,
        TradingSignal signal,
        CancellationToken cancellationToken = default);

    Task PublishBalanceAsync(
        int userId,
        decimal balance,
        string currency,
        bool useFutures,
        CancellationToken cancellationToken = default);
}
