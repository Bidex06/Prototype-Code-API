using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using TradingBotEngine.API.DTOs.Trading;
using TradingBotEngine.Core.Models;
using TradingBotEngine.Services;

namespace TradingBotEngine.API.Hubs;

/// <summary>
/// Publishes realtime trading events to the authenticated user's SignalR group.
/// A failed realtime notification must never fail or block the underlying trading operation.
/// </summary>
public sealed class SignalRTradingRealtimePublisher : ITradingRealtimePublisher
{
    private readonly IHubContext<TradingHub> _hubContext;
    private readonly ILogger<SignalRTradingRealtimePublisher> _logger;

    public SignalRTradingRealtimePublisher(
        IHubContext<TradingHub> hubContext,
        ILogger<SignalRTradingRealtimePublisher> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task PublishSignalAsync(
        int userId,
        TradingSignal signal,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new SignalResponseDto
            {
                Id = 0,
                Symbol = signal.Symbol,
                Action = signal.Action,
                Price = signal.Price,
                StopLoss = signal.StopLoss,
                TakeProfit = signal.TakeProfit,
                Confidence = signal.Confidence,
                Pattern = signal.Pattern,
                Rsi = signal.Rsi,
                Trend = signal.Trend,
                Support = signal.Support,
                Resistance = signal.Resistance,
                Reason = signal.Reason,
                GeneratedAt = signal.GeneratedAt,
                WasExecuted = false
            };

            await _hubContext.Clients
                .Group(TradingHub.UserGroup(userId))
                .SendAsync("NewSignal", payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "P1.10 SignalR signal publication failed for user {UserId}, symbol {Symbol}.",
                userId,
                signal.Symbol);
        }
    }

    public async Task PublishBalanceAsync(
        int userId,
        decimal balance,
        string currency,
        bool useFutures,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new BalanceResponseDto
            {
                Balance = balance,
                Currency = currency,
                UseFutures = useFutures
            };

            await _hubContext.Clients
                .Group(TradingHub.UserGroup(userId))
                .SendAsync("BalanceUpdated", payload, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "P1.10 SignalR balance publication failed for user {UserId}, currency {Currency}, futures={UseFutures}.",
                userId,
                currency,
                useFutures);
        }
    }
}
