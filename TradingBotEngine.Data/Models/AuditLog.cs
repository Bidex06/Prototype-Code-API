using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models
{
    public class AuditLog
    {
        [Key]
        public int Id { get; set; }
        
        public int? UserId { get; set; }
        public User User { get; set; }
        
        [Required, MaxLength(50)]
        public string Action { get; set; }  // "Trade_Entered", "Trade_Closed", "Signal_Generated", etc.
        
        [Required]
        public string Details { get; set; }
        
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
} 