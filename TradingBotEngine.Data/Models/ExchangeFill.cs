using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models
{
    public class ExchangeFill
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int ExchangeOrderId { get; set; }

        public ExchangeOrder ExchangeOrder { get; set; }

        [Required, MaxLength(100)]
        public string ExchangeFillId { get; set; }

        public decimal Quantity { get; set; }

        public decimal Price { get; set; }

        public decimal Fee { get; set; }

        [MaxLength(20)]
        public string? FeeAsset { get; set; }

        public DateTime FilledAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}