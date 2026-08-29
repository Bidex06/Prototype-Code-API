using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models
{
    public class RiskSetting
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int UserId { get; set; }
        public User User { get; set; }
        
        [Required, MaxLength(20)]
        public string RiskLevel { get; set; }  // "Conservative", "Balanced", "Aggressive"
        
        public decimal RiskPerTrade { get; set; }  // 0.5%, 1%, 2%
        public int MaxOpenTrades { get; set; }  // 2, 5, 10
        public decimal DailyLossLimit { get; set; }  // 5% default
        public decimal MaxDrawdown { get; set; }  // 10% default
        
        public decimal? FixedLotSize { get; set; }
        public bool UseFixedLotSize { get; set; }
        
        public decimal StopLossMinPips { get; set; }
        public decimal TakeProfitMinPips { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}