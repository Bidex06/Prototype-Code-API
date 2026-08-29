using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models
{
    public class Payment
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int UserId { get; set; }
        public User User { get; set; }
        
        [Required, MaxLength(50)]
        public string TransactionId { get; set; }
        
        [Required, MaxLength(20)]
        public string PaymentMethod { get; set; }  // "Card", "BankTransfer", "Crypto"
        
        [Required]
        public decimal Amount { get; set; }
        
        [Required, MaxLength(3)]
        public string Currency { get; set; }
        
        [Required, MaxLength(20)]
        public string Status { get; set; }  // "Pending", "Completed", "Failed"
        
        [MaxLength(50)]
        public string? Gateway { get; set; }  // "Flutterwave", "NOWPayments"
        public string? GatewayReference { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }
} 