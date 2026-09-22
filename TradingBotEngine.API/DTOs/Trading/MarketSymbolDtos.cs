namespace TradingBotEngine.API.DTOs.Trading;

public sealed class MarketSymbolResponseDto
{
    public string Symbol { get; set; } = string.Empty;
    public string BaseAsset { get; set; } = string.Empty;
    public string QuoteAsset { get; set; } = string.Empty;
    public string Exchange { get; set; } = string.Empty;
    public string Market { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}
