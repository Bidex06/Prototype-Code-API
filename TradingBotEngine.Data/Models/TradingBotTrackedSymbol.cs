using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models;

public class TradingBotTrackedSymbol
{
    [Required]
    public int TradingBotId { get; set; }
    public TradingBot TradingBot { get; set; } = null!;

    [Required]
    public int TrackedSymbolId { get; set; }
    public TrackedSymbol TrackedSymbol { get; set; } = null!;
}
