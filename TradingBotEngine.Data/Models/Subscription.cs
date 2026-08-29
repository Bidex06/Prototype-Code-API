using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TradingBotEngine.Data.Models
{
    public class Subscription
    {
        [Key]
        public int Id { get; set; }
        
        [Required]
        public int UserId { get; set; }  // 👈 FOREIGN KEY
        
        [ForeignKey(nameof(UserId))]
        public User User { get; set; }  // 👈 NAVIGATION PROPERTY
        
        [Required, MaxLength(20)]
        public string Plan { get; set; }  // "Monthly", "Annual"
        
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsTrial { get; set; }
        public DateTime? TrialEndDate { get; set; }
        
        [MaxLength(50)]
        public string? PaymentReference { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}