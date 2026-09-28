using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models;

public class TradingBot
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string Strategy { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string Timeframe { get; set; } = "1h";

    public int? BrokerConnectionId { get; set; }
    public BrokerConnection? BrokerConnection { get; set; }

    public bool UseFutures { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsRunning { get; set; }

    // Per-bot auto-execution override. This sits UNDERNEATH the existing
    // User.IsAutoTradeEnabled global switch - both must be true for a bot
    // to auto-execute. Defaults to true so existing bots keep behaving
    // exactly as they did before this field existed (gated only by the
    // user-level switch, as today).
    public bool AutoTradeEnabled { get; set; } = true;

    // Defaults to Auto for both legs so existing bots keep their current
    // behavior unchanged: today every bot implicitly trusts the signal
    // generator's own calculated stop-loss/take-profit.
    public RiskManagementMode StopLossMode { get; set; } = RiskManagementMode.Auto;
    public decimal? StopLossPercent { get; set; }

    public RiskManagementMode TakeProfitMode { get; set; } = RiskManagementMode.Auto;
    public decimal? TakeProfitPercent { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastStartedAt { get; set; }
    public DateTime? LastStoppedAt { get; set; }

    public ICollection<TradingBotTrackedSymbol> TrackedSymbols { get; set; } = new List<TradingBotTrackedSymbol>();
}
