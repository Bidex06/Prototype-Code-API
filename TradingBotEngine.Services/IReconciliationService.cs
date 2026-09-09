using TradingBotEngine.Data.Models;

namespace TradingBotEngine.Services
{
    public interface IReconciliationService
    {
        Task<bool> ReconcileOrderAsync(
            int userId,
            int exchangeOrderId);

        Task<int> ReconcilePendingOrdersAsync(
            int userId);
    }
}