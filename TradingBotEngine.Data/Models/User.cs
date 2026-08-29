using System.ComponentModel.DataAnnotations;

namespace TradingBotEngine.Data.Models
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string LastName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }

        public bool IsEmailVerified { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsAutoTradeEnabled { get; set; } = false;

        [Required, MaxLength(30)]
        public string Role { get; set; } = "User";

        public Subscription Subscription { get; set; } = null!;

        public ICollection<Trade> Trades { get; set; } = new List<Trade>();
        public ICollection<Position> Positions { get; set; } = new List<Position>();
        public ICollection<Signal> Signals { get; set; } = new List<Signal>();
        public ICollection<BrokerConnection> BrokerConnections { get; set; } = new List<BrokerConnection>();
        public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
        public ICollection<TrackedSymbol> TrackedSymbols { get; set; } = new List<TrackedSymbol>();
        public RiskSetting RiskSetting { get; set; } = null!;

        public long? TelegramChatId { get; set; }
        public string? TelegramUsername { get; set; }
    }
}
