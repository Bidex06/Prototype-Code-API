using Microsoft.EntityFrameworkCore;
using Telegram.Bot.Types;
using TradingBotEngine.Data;

namespace TradingBotEngine.Services
{
    /// <summary>
    /// Telegram is deliberately read-only for credential and order safety.
    /// API keys, API secrets and account passwords must never be transmitted through chat.
    /// Broker connections and live trading controls are managed through the authenticated web API.
    /// </summary>
    public class TelegramCommandHandler
    {
        private readonly ApplicationDbContext _context;
        private readonly BrokerService _brokerService;

        public TelegramCommandHandler(ApplicationDbContext context, BrokerService brokerService)
        {
            _context = context;
            _brokerService = brokerService;
        }

        public async Task<string> HandleCommandAsync(long chatId, string username, string command, Message message)
        {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return "Type /help for available commands.";

            return parts[0].ToLowerInvariant() switch
            {
                "/start" => await HandleStartAsync(chatId, username),
                "/logout" => await HandleLogoutAsync(chatId),
                "/balance" => await HandleBalanceAsync(chatId),
                "/positions" => await HandlePositionsAsync(chatId),
                "/help" => HelpMessage(),
                "/login" or "/connect_binance" or "/connect_bybit" or "/trade"
                    => "🔒 For security, password/API-key entry and order placement are disabled in Telegram. Use the authenticated web dashboard instead.",
                _ => "❌ Unknown command. Type /help for available commands."
            };
        }

        private async Task<string> HandleStartAsync(long chatId, string username)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId);
            if (user == null)
            {
                return "👋 Welcome to Trading Bot Engine.\n\n" +
                       "🔐 Link Telegram from the authenticated web dashboard.\n" +
                       "⚠️ Never send your account password, API key, or API secret in Telegram.";
            }

            return $"👋 Welcome back, {user.FirstName}!\n\n" +
                   $"📊 Account: {user.Email}\n" +
                   $"🔗 {await GetConnectedBrokersAsync(user.Id)}\n\n" +
                   "Type /help for available commands.";
        }

        private async Task<string> HandleLogoutAsync(long chatId)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId);
            if (user == null)
                return "❌ Your Telegram is not linked.";

            user.TelegramChatId = null;
            user.TelegramUsername = null;
            await _context.SaveChangesAsync();
            return "✅ Telegram has been unlinked from your account.";
        }

        private async Task<string> HandleBalanceAsync(long chatId)
        {
            var user = await GetUserByChatIdAsync(chatId);
            if (user == null)
                return "❌ Link Telegram from the web dashboard first.";

            var broker = await _context.BrokerConnections
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == user.Id && b.IsActive && b.IsConnected);

            if (broker == null)
                return "❌ No active broker connection.";

            var balance = await _brokerService.GetBalanceAsync(user.Id, "USDT", false);
            return $"💰 Balance\n\nExchange: {broker.BrokerName}\nBalance: ${balance:F2} USDT\nMode: {(broker.IsTestnet ? "TESTNET" : "LIVE")}";
        }

        private async Task<string> HandlePositionsAsync(long chatId)
        {
            var user = await GetUserByChatIdAsync(chatId);
            if (user == null)
                return "❌ Link Telegram from the web dashboard first.";

            var positions = await _context.Positions
                .AsNoTracking()
                .Where(p => p.UserId == user.Id && p.Status == "Open")
                .ToListAsync();

            if (positions.Count == 0)
                return "📭 No open positions.";

            var response = "📊 Open Positions\n\n";
            foreach (var position in positions)
            {
                response += $"🔹 {position.Symbol}\n" +
                            $"Direction: {position.Direction}\n" +
                            $"Entry: ${position.EntryPrice:F2}\n" +
                            $"Current: ${position.CurrentPrice:F2}\n" +
                            $"PnL: ${position.UnrealizedPnL:F2}\n\n";
            }

            return response;
        }

        private async Task<TradingBotEngine.Data.Models.User?> GetUserByChatIdAsync(long chatId)
        {
            return await _context.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId && u.IsActive);
        }

        private async Task<string> GetConnectedBrokersAsync(int userId)
        {
            var brokers = await _context.BrokerConnections
                .AsNoTracking()
                .Where(b => b.UserId == userId && b.IsActive && b.IsConnected)
                .Select(b => b.BrokerName)
                .ToListAsync();

            return brokers.Count > 0 ? $"Connected: {string.Join(", ", brokers)}" : "No brokers connected.";
        }

        private static string HelpMessage() =>
            "📚 Available Commands\n\n" +
            "🔐 /start - Show account status\n" +
            "/logout - Unlink Telegram\n\n" +
            "📊 /balance - Check connected broker balance\n" +
            "/positions - View open positions\n\n" +
            "🔒 Broker credentials and order placement are intentionally disabled in Telegram. Use the authenticated web dashboard.";
    }
}
