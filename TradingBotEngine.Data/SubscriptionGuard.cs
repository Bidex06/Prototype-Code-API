using TradingBotEngine.Data.Models;

namespace TradingBotEngine.Data
{
    /// <summary>
    /// Single source of truth for "does this user currently have bot/trading access".
    /// Used at every point that lets a user create, start, or execute a trade,
    /// so a login change never has to be re-synced with these rules by hand.
    /// </summary>
    public static class SubscriptionGuard
    {
        public const string ExpiredMessage =
            "Your subscription has expired. Renew to create or run trading bots.";

        public static bool HasActiveSubscription(User user) =>
            user.Subscription != null &&
            user.Subscription.IsActive &&
            user.Subscription.EndDate > DateTime.UtcNow;
    }
}