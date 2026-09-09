using Binance.Net.Clients;
using Bybit.Net.Clients;
using Bybit.Net.Enums;

namespace TradingBotEngine.Services;

public static class ExchangeApiProbe
{
    public static async Task ProbeAsync()
    {
        var binance = new BinanceRestClient();

        var binanceSpotInfo =
            await binance.SpotApi.ExchangeData
                .GetExchangeInfoAsync("BTCUSDT");

        var binanceSpotTicker =
            await binance.SpotApi.ExchangeData
                .GetTickerAsync("BTCUSDT");

        var bybit = new BybitRestClient();

        var bybitSpotInfo =
            await bybit.V5Api.ExchangeData
                .GetSpotSymbolsAsync("BTCUSDT");

        var bybitSpotTicker =
            await bybit.V5Api.ExchangeData
                .GetSpotTickersAsync("BTCUSDT");

        var bybitLinearInfo =
            await bybit.V5Api.ExchangeData
                .GetLinearInverseSymbolsAsync(
                    Category.Linear,
                    "BTCUSDT");

        var bybitLinearTicker =
            await bybit.V5Api.ExchangeData
                .GetLinearInverseTickersAsync(
                    Category.Linear,
                    "BTCUSDT");
    }
}