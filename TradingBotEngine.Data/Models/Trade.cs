using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models
{
    public class Trade
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int UserId { get; set; }
        public User User { get; set; }
        
        [Required, MaxLength(20)]
        public string Symbol { get; set; }  // e.g., "EURUSD", "BTCUSDT"
        
        [Required, MaxLength(10)]
        public string Direction { get; set; }  // "Buy" or "Sell"

        // The bot that opened this trade (null for manual or older trades).
        public int? BotId { get; set; }
        
        public decimal EntryPrice { get; set; }
        public decimal? ExitPrice { get; set; }
        public decimal Quantity { get; set; }
        public decimal? StopLoss { get; set; }
        public decimal? TakeProfit { get; set; }
        
        public DateTime EntryTime { get; set; }
        public DateTime? ExitTime { get; set; }
        
        public decimal? ProfitLoss { get; set; }
        public decimal? ProfitLossPercentage { get; set; }
        
        [MaxLength(100)]
        public string Status { get; set; }  // "Open", "Closed", "Cancelled"
        
        [MaxLength(500)]
        public string? Reason { get; set; }  // Why the trade was entered
        public string? Notes { get; set; }
        
        // Broker info
        [MaxLength(50)]
        public string BrokerName { get; set; }
        public string? OrderId { get; set; }
        
        public string? IdempotencyKey { get; set; }

        [MaxLength(100)]
        public string? StopLossOrderId { get; set; }

        [MaxLength(100)]
        public string? TakeProfitOrderId { get; set; }

        [MaxLength(30)]
        public string ProtectionStatus { get; set; } = "NotRequired";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
