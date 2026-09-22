using Microsoft.AspNetCore.SignalR;
using TradingBotEngine.Core;

namespace TradingBotEngine.API.Hubs;

public sealed class TradingEventPublisher : ITradingEventPublisher
{
    private readonly IHubContext<TradingHub> _hubContext;

    public TradingEventPublisher(IHubContext<TradingHub> hubContext)
    {
        _hubContext = hubContext;
    }

    public Task PublishNewSignalAsync(
        int userId,
        object signal,
        CancellationToken cancellationToken = default)
    {
        return _hubContext
            .Clients
            .User(userId.ToString())
            .SendAsync(
                "NewSignal",
                signal,
                cancellationToken);
    }
}