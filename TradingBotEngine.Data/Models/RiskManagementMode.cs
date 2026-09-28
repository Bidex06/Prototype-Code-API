namespace TradingBotEngine.Data.Models;

/// <summary>
/// How a bot's protective exit level (stop-loss or take-profit) is determined.
/// </summary>
public enum RiskManagementMode
{
    /// <summary>The strategy/signal generator's own calculated level is used as-is.</summary>
    Auto = 0,

    /// <summary>The user's configured percent (off entry price) is used instead of the signal's level.</summary>
    Manual = 1,

    /// <summary>No protective order is placed for this leg at all.</summary>
    None = 2
}
