using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models
{
    public class ExchangeBalance
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }

        public User User { get; set; }

        [Required, MaxLength(50)]
        public string BrokerName { get; set; }

        [Required, MaxLength(20)]
        public string Currency { get; set; }

        public decimal AvailableBalance { get; set; }

        public decimal TotalBalance { get; set; }

        public bool IsTestnet { get; set; }

        public bool IsFutures { get; set; }

        public DateTime CapturedAt { get; set; } = DateTime.UtcNow;
    }
}