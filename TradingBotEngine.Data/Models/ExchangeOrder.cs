using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models
{
    public class ExchangeOrder
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        public User User { get; set; }

        [Required]
        public int TradeId { get; set; }

        public Trade Trade { get; set; }

        [Required, MaxLength(50)]
        public string BrokerName { get; set; }

        [Required, MaxLength(100)]
        public string ExchangeOrderId { get; set; }

        [Required, MaxLength(20)]
        public string Symbol { get; set; }

        [Required, MaxLength(10)]
        public string Side { get; set; }

        [Required, MaxLength(20)]
        public string OrderType { get; set; }

        public decimal RequestedQuantity { get; set; }

        public decimal FilledQuantity { get; set; }

        public decimal AverageFillPrice { get; set; }

        [MaxLength(30)]
        public string Status { get; set; }

        public bool IsTestnet { get; set; }


        public bool IsFutures { get; set; }
        

        [MaxLength(500)]
        public string? FailureReason { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public DateTime? LastReconciledAt { get; set; }

        public ICollection<ExchangeFill> Fills { get; set; } = new List<ExchangeFill>();
    }
}