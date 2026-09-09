namespace TradingBotEngine.Services;

public sealed class ExchangeOrderRules
{
    public decimal MinQuantity { get; init; }
    public decimal MaxQuantity { get; init; }
    public decimal QuantityStep { get; init; }
    public decimal MinNotional { get; init; }
    public decimal MaxLeverage { get; init; } = 1m;
    public decimal ReferencePrice { get; init; }
}