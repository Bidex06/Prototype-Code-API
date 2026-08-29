using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models
{
    /// <summary>
    /// A symbol explicitly tracked by a user for signal generation/auto-trading.
    /// </summary>
    public class TrackedSymbol
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserId { get; set; }
        public User User { get; set; } = null!;

        [Required, MaxLength(30)]
        public string Symbol { get; set; } = string.Empty;

        [Required, MaxLength(30)]
        public string Exchange { get; set; } = "Binance";

        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}
