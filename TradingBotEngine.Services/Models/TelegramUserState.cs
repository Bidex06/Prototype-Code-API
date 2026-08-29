namespace TradingBotEngine.Services.Models
{
    public class TelegramUserState
    {
        public string State { get; set; } = string.Empty; // "awaiting_api_key", "awaiting_api_secret"
        public string Exchange { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string ApiKey { get; set; } = string.Empty;
        public string ApiSecret { get; set; } = string.Empty;
    }
} 