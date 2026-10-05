using System.Text.RegularExpressions;
using FluentAssertions;
using TradingBotEngine.Services;
using Xunit;

namespace TradingBotEngine.Tests;

public class StopRiskSizingAndClientOrderIdTests
{
    // Regression for the sizing bug: loss at the stop must never exceed the configured
    // risk, for any stop distance, leverage or lot step (rounding is always DOWN).
    [Theory]
    [InlineData(1000, 1, 60000, 58800, 1, 0.001)]
    [InlineData(1000, 1, 60000, 54000, 1, 0.001)]
    [InlineData(1000, 1, 60000, 58800, 10, 0.001)]
    [InlineData(5000, 2, 3000, 2850, 5, 0.01)]
    [InlineData(250, 1, 1.5, 1.4, 1, 1)]
    public void CalculateQuantity_LossAtStop_NeverExceedsConfiguredRisk(
        decimal balance, decimal riskPercent, decimal entry, decimal stop, decimal leverage, decimal step)
    {
        var quantity = OrderRiskSizer.CalculateQuantity(
            balance, riskPercent, entry, stop, leverage,
            minQuantity: step, maxQuantity: decimal.MaxValue, quantityStep: step);

        var lossAtStop = quantity * Math.Abs(entry - stop);
        var allowedLoss = balance * riskPercent / 100m;

        lossAtStop.Should().BeLessThanOrEqualTo(allowedLoss);
        (quantity * entry).Should().BeLessThanOrEqualTo(balance * leverage);
    }

    [Fact]
    public void CalculateQuantity_Throws_WhenRiskCannotBeMetAtMinimumLot()
    {
        // $10 risk with a 20% stop on BTC is smaller than one 0.001 lot: must refuse, not over-risk.
        var act = () => OrderRiskSizer.CalculateQuantity(
            1000m, 1m, 60000m, 48000m, 10m,
            minQuantity: 0.001m, maxQuantity: decimal.MaxValue, quantityStep: 0.001m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void BuildClientOrderId_IsDeterministic_AndAcceptedByBinanceAndBybit()
    {
        var key = "auto:5:BTCUSDT:15m:20261002123000";

        var first = BrokerService.BuildClientOrderId(7, key);
        var second = BrokerService.BuildClientOrderId(7, key);

        first.Should().Be(second);
        first.Length.Should().BeLessThanOrEqualTo(36);
        Regex.IsMatch(first, "^[A-Za-z0-9_-]{1,36}$").Should().BeTrue();
    }

    [Fact]
    public void BuildClientOrderId_DiffersPerUser_AndPerIdempotencyKey()
    {
        var key = "auto:5:BTCUSDT:15m:20261002123000";

        BrokerService.BuildClientOrderId(7, key)
            .Should().NotBe(BrokerService.BuildClientOrderId(8, key));

        BrokerService.BuildClientOrderId(7, key)
            .Should().NotBe(BrokerService.BuildClientOrderId(7, key + "1"));
    }
}
