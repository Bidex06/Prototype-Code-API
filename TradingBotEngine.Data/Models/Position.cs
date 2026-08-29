using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models
{
    public class Position
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int UserId { get; set; }
        public User User { get; set; }
        
        [Required, MaxLength(20)]
        public string Symbol { get; set; }
        
        [Required, MaxLength(10)]
        public string Direction { get; set; }  // "Long" or "Short"
        
        public decimal EntryPrice { get; set; }
        public decimal CurrentPrice { get; set; }
        public decimal Quantity { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TakeProfit { get; set; }
        
        public decimal UnrealizedPnL { get; set; }
        public decimal UnrealizedPnLPercentage { get; set; }
        
        [MaxLength(20)]
        public string Status { get; set; }  // "Open", "Closed"
        
        public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ClosedAt { get; set; }
    }
}