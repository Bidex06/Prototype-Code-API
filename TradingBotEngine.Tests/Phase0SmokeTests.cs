using TradingBotEngine.Core;
using TradingBotEngine.Core.Models;

namespace TradingBotEngine.Tests;

public class Phase0SmokeTests
{
    [Fact]
    public async Task SignalGenerator_ReturnsHoldForNeutralConditions()
    {
        var candles = Enumerable.Range(0, 200)
            .Select(i => new Candle
            {
                Open = 100,
                High = 101,
                Low = 99,
                Close = 100,
                Volume = 1000
            })
            .ToList();

        var indicators = new Dictionary<string, object>
        {
            ["RSI"] = 50m,
            ["MA50"] = 100m,
            ["MA200"] = 100m,
            ["Pattern"] = "None",
            ["Trend"] = "Sideways",
            ["Support"] = 99m,
            ["Resistance"] = 101m,
            ["Volume"] = 1000m,
            ["AverageVolume"] = 1000m,
            ["TrendStrength"] = 20m
        };

        var generator = new SignalGenerator();
        var signal = await generator.GenerateSignal("BTCUSDT", candles, indicators);

        Assert.Equal("HOLD", signal.Action);
        Assert.Equal("BTCUSDT", signal.Symbol);
    }
}
