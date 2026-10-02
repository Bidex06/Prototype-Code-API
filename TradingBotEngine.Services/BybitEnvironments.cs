using Bybit.Net;

namespace TradingBotEngine.Services;

/// <summary>
/// Chooses the Bybit server for a connection.
/// When <see cref="UseDemoTrading"/> is on, a Bybit connection marked "testnet" talks to
/// Bybit Demo Trading (api-demo.bybit.com) instead of the separate Bybit testnet site.
/// Demo API keys are created in a normal Bybit account while it is switched to Demo Trading.
/// Live connections always use the live server.
/// </summary>
public static class BybitEnvironments
{
    // Set once at startup from configuration: Bybit:UseDemoTrading
    public static bool UseDemoTrading { get; set; }

    private static readonly BybitEnvironment Demo =
        (BybitEnvironment)BybitEnvironment.CreateCustom(
            "demo",
            "https://api-demo.bybit.com",
            "wss://stream-demo.bybit.com");

    // Used for orders, balances and positions.
    public static BybitEnvironment ForTrading(bool isTestnet)
    {
        if (!isTestnet)
            return BybitEnvironment.Live;

        return UseDemoTrading ? Demo : BybitEnvironment.Testnet;
    }

    // Used for public market data (candles, symbol rules). Demo trading uses live prices.
    public static BybitEnvironment ForMarketData(bool isTestnet)
    {
        if (!isTestnet)
            return BybitEnvironment.Live;

        return UseDemoTrading ? BybitEnvironment.Live : BybitEnvironment.Testnet;
    }
}
