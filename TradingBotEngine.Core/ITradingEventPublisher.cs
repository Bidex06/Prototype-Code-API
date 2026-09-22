namespace TradingBotEngine.Core;

public interface ITradingEventPublisher
{
    Task PublishNewSignalAsync(int userId, object signal, CancellationToken cancellationToken = default);
}
