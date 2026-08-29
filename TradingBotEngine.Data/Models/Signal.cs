using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models
{
    public class Signal
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int UserId { get; set; }
        public User User { get; set; }
        
        [Required, MaxLength(20)]
        public string Symbol { get; set; }
        
        [Required, MaxLength(10)]
        public string Action { get; set; }  // "BUY", "SELL", "HOLD"
        
        [Required]
        public decimal Price { get; set; }
        
        public decimal? StopLoss { get; set; }
        public decimal? TakeProfit { get; set; }
        
        public decimal Confidence { get; set; }  // 0-100
        
        // Analysis details
        public string? Pattern { get; set; }  // "Head & Shoulders", "Double Bottom", etc.
        public decimal? Rsi { get; set; }
        public string? Trend { get; set; }  // "Bullish", "Bearish", "Sideways"
        public decimal? Support { get; set; }
        public decimal? Resistance { get; set; }
        public string? Indicators { get; set; }  // JSON of indicator values
        public string? Reason { get; set; }  // Why this signal was generated
        
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public bool WasExecuted { get; set; }
        public DateTime? ExecutedAt { get; set; }
        public int? TradeId { get; set; }
        public Trade ExecutedTrade { get; set; }
    }
} 