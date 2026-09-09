using FluentAssertions;
using TradingBotEngine.Services;
using Xunit;

namespace TradingBotEngine.Tests;

public class OrderRiskSizerTests
{
    [Fact]
    public void CalculateQuantity_ShouldSizePositionFromRisk()
    {
        // Arrange
        var balance = 1000m;
        var riskPercent = 1m;       // $10 risk
        var entryPrice = 100m;
        var stopLoss = 90m;         // $10 distance
        var leverage = 1m;

        // $10 risk / $10 stop distance = 1 unit
        var quantity = OrderRiskSizer.CalculateQuantity(
            balance,
            riskPercent,
            entryPrice,
            stopLoss,
            leverage,
            minQuantity: 0.001m,
            maxQuantity: 1000m,
            quantityStep: 0.001m);

        // Assert
        quantity.Should().Be(1m);
    }

    [Fact]
    public void CalculateQuantity_ShouldRoundDownToExchangeStep()
    {
        var quantity = OrderRiskSizer.CalculateQuantity(
            balance: 1000m,
            riskPerTradePercent: 1m,
            entryPrice: 100m,
            stopLoss: 93m,
            leverage: 1m,
            minQuantity: 0.01m,
            maxQuantity: 1000m,
            quantityStep: 0.01m);

        // 10 / 7 = 1.428571...
        // Must round DOWN, never up.
        quantity.Should().Be(1.42m);
    }

    [Fact]
    public void CalculateQuantity_ShouldReject_WhenCalculatedQuantityIsBelowMinimum()
    {
        var action = () =>
            OrderRiskSizer.CalculateQuantity(
                balance: 100m,
                riskPerTradePercent: 1m,
                entryPrice: 100m,
                stopLoss: 1m,
                leverage: 1m,
                minQuantity: 2m,
                maxQuantity: 1000m,
                quantityStep: 1m);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Calculated quantity is below the exchange minimum.");
    }

    [Fact]
    public void CalculateQuantity_ShouldReject_WhenStopLossEqualsEntry()
    {
        var action = () =>
            OrderRiskSizer.CalculateQuantity(
                balance: 1000m,
                riskPerTradePercent: 1m,
                entryPrice: 100m,
                stopLoss: 100m,
                leverage: 1m,
                minQuantity: 0.001m,
                maxQuantity: 1000m,
                quantityStep: 0.001m);

        action.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("Stop loss must be different from entry price.");
    }
}