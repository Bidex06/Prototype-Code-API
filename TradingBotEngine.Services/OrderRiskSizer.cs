namespace TradingBotEngine.Services;

public sealed class OrderRiskSizer
{
    public static decimal CalculateQuantity(
        decimal balance,
        decimal riskPerTradePercent,
        decimal entryPrice,
        decimal stopLoss,
        decimal leverage,
        decimal minQuantity,
        decimal maxQuantity,
        decimal quantityStep)
    {
        if (balance <= 0)
            throw new InvalidOperationException("Available balance is zero.");

        if (riskPerTradePercent <= 0 || riskPerTradePercent > 100)
            throw new InvalidOperationException("Invalid risk-per-trade setting.");

        if (entryPrice <= 0)
            throw new InvalidOperationException("Entry price must be positive.");

        if (stopLoss <= 0)
            throw new InvalidOperationException("Stop loss must be positive.");

        if (leverage <= 0)
            throw new InvalidOperationException("Leverage must be positive.");

        if (minQuantity <= 0 || maxQuantity < minQuantity)
            throw new InvalidOperationException("Invalid exchange quantity rules.");

        if (quantityStep <= 0)
            throw new InvalidOperationException("Invalid exchange quantity step.");

        var stopDistance = Math.Abs(entryPrice - stopLoss);

        if (stopDistance <= 0)
            throw new InvalidOperationException(
                "Stop loss must be different from entry price.");

        var riskAmount =
            balance * (riskPerTradePercent / 100m);

        var rawQuantity =
            riskAmount / stopDistance;

        // Never allow the calculated position to require more
        // margin than the available balance.
        var leverageQuantity =
            (balance * leverage) / entryPrice;

        var quantity =
            Math.Min(rawQuantity, leverageQuantity);

        // Always round DOWN so rounding can never increase risk.
        quantity =
            Math.Floor(quantity / quantityStep) * quantityStep;

        if (quantity < minQuantity)
            throw new InvalidOperationException(
                "Calculated quantity is below the exchange minimum.");

        if (quantity > maxQuantity)
            quantity = maxQuantity;

        quantity =
            Math.Floor(quantity / quantityStep) * quantityStep;

        if (quantity < minQuantity)
            throw new InvalidOperationException(
                "Calculated quantity is below the exchange minimum.");

        return quantity;
    }
}