using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Bybit.Net;
using Bybit.Net.Clients;
using CryptoExchange.Net.Authentication;
using Microsoft.EntityFrameworkCore;
using TradingBotEngine.Data;
using TradingBotEngine.Data.Models;

namespace TradingBotEngine.Services
{
    /// <summary>
    /// Creates exchange clients from encrypted, user-owned connection metadata on demand.
    /// No exchange client is kept in a scoped in-memory dictionary, so API restarts do not
    /// silently disconnect users from their persisted broker configuration.
    /// </summary>
    public class BrokerService
    {
        private readonly ApplicationDbContext _context;
        private readonly ICredentialProtector _protector;

        public BrokerService(ApplicationDbContext context, ICredentialProtector protector)
        {
            _context = context;
            _protector = protector;
        }

        public async Task<bool> ConnectAsync(int userId, BrokerConnection connection)
        {
            var persisted = await _context.BrokerConnections
                .FirstOrDefaultAsync(b => b.UserId == userId && b.BrokerName == connection.BrokerName);

            if (persisted == null)
            {
                persisted = connection;
                persisted.UserId = userId;
                persisted.IsTestnet = true;
                persisted.IsLiveTradingEnabled = false;
                persisted.LiveTradingOptInAt = null;
                persisted.ApiKey = _protector.Protect(connection.ApiKey);
                persisted.ApiSecret = string.IsNullOrWhiteSpace(connection.ApiSecret)
                    ? null
                    : _protector.Protect(connection.ApiSecret);
                _context.BrokerConnections.Add(persisted);
            }

            try
            {
                var client = CreateClient(persisted);
                var connected = await VerifyConnectionAsync(client, persisted);

                persisted.IsConnected = connected;
                persisted.IsActive = connected || persisted.IsActive;
                persisted.LastConnectedAt = connected ? DateTime.UtcNow : persisted.LastConnectedAt;
                persisted.UpdatedAt = DateTime.UtcNow;

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = connected ? "BrokerConnected" : "BrokerConnectionFailed",
                    Details = $"Broker {persisted.BrokerName} connection attempt completed in {(persisted.IsTestnet ? "testnet" : "live")} mode.",
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
                return connected;
            }
            catch
            {
                persisted.IsConnected = false;
                persisted.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return false;
            }
        }

        public async Task<bool> DisconnectAsync(int userId, string? brokerName = null)
        {
            var connections = await _context.BrokerConnections
                .Where(b => b.UserId == userId && b.IsActive &&
                            (brokerName == null || b.BrokerName == brokerName))
                .ToListAsync();

            if (connections.Count == 0)
                return false;

            foreach (var connection in connections)
            {
                connection.IsConnected = false;
                connection.LastDisconnectedAt = DateTime.UtcNow;
                connection.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<bool> IsConnectedAsync(int userId, string? brokerName = null)
        {
            return await _context.BrokerConnections.AnyAsync(b =>
                b.UserId == userId && b.IsActive && b.IsConnected &&
                (brokerName == null || b.BrokerName == brokerName));
        }

        public async Task<IReadOnlyList<BrokerConnectionStatus>> GetConnectionsAsync(int userId)
        {
            return await _context.BrokerConnections
                .AsNoTracking()
                .Where(b => b.UserId == userId && b.IsActive)
                .Select(b => new BrokerConnectionStatus
                {
                    Id = b.Id,
                    BrokerName = b.BrokerName,
                    IsConnected = b.IsConnected,
                    IsTestnet = b.IsTestnet,
                    IsLiveTradingEnabled = b.IsLiveTradingEnabled,
                    LastConnectedAt = b.LastConnectedAt,
                    LastDisconnectedAt = b.LastDisconnectedAt
                })
                .ToListAsync();
        }

        public async Task<bool> SetLiveTradingOptInAsync(int userId, int connectionId, bool enabled)
        {
            var connection = await _context.BrokerConnections
                .FirstOrDefaultAsync(b => b.Id == connectionId && b.UserId == userId && b.IsActive);

            if (connection == null)
                return false;

            if (enabled)
            {
                connection.IsTestnet = false;
                connection.IsLiveTradingEnabled = true;
                connection.LiveTradingOptInAt = DateTime.UtcNow;
            }
            else
            {
                connection.IsTestnet = true;
                connection.IsLiveTradingEnabled = false;
                connection.LiveTradingOptInAt = null;
            }

            connection.UpdatedAt = DateTime.UtcNow;
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = enabled ? "LiveTradingEnabled" : "LiveTradingDisabled",
                Details = $"Broker connection {connection.Id} live-trading state changed to {enabled}.",
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<decimal> GetBalanceAsync(int userId, string currency = "USDT", bool useFutures = false)
        {
            var connection = await GetConnectionAsync(userId);
            if (connection == null)
                return 0;

            try
            {
                var client = CreateClient(connection);

                if (client is BinanceRestClient binance)
                {
                    if (useFutures)
                    {
                        var result = await binance.UsdFuturesApi.Account.GetAccountInfoAsync();
                        if (result.Success)
                            return result.Data.Assets.FirstOrDefault(a => a.Asset == currency)?.WalletBalance ?? 0;
                    }
                    else
                    {
                        var result = await binance.SpotApi.Account.GetAccountInfoAsync();
                        if (result.Success)
                            return result.Data.Balances.FirstOrDefault(a => a.Asset == currency)?.Available ?? 0;
                    }
                }

                if (client is BybitRestClient bybit)
                {
                    var accountType = useFutures
                        ? Bybit.Net.Enums.AccountType.Contract
                        : Bybit.Net.Enums.AccountType.Spot;

                    var result = await bybit.V5Api.Account.GetBalancesAsync(accountType, currency, CancellationToken.None);
                    if (result.Success)
                        return result.Data.List?.FirstOrDefault()?.Assets?.FirstOrDefault(a => a.Asset == currency)?.Free ?? 0;
                }
            }
            catch
            {
                // Do not leak exchange exception details through the API.
            }

            return 0;
        }

        public async Task<BrokerOrderResult> PlaceOrderAsync(
            int userId,
            string symbol,
            string direction,
            decimal quantity,
            string orderType = "Market",
            decimal? stopLoss = null,
            decimal? takeProfit = null,
            decimal? entryPrice = null,
            bool useFutures = false,
            string? idempotencyKey = null)
        {
            if (string.IsNullOrWhiteSpace(symbol) || quantity <= 0)
                return BrokerOrderResult.Fail("Invalid order parameters.");

            if (!string.Equals(direction, "BUY", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(direction, "SELL", StringComparison.OrdinalIgnoreCase))
                return BrokerOrderResult.Fail("Invalid order side.");

            var connection = await GetConnectionAsync(userId);
            if (connection == null || !connection.IsConnected)
                return BrokerOrderResult.Fail("No active broker connection.");

            // New connections are testnet by default. Live orders require an explicit opt-in.
            if (!connection.IsTestnet && !connection.IsLiveTradingEnabled)
                return BrokerOrderResult.Fail("Live trading is not explicitly enabled for this connection.");

            var client = CreateClient(connection);
            var normalizedSide = direction.ToUpperInvariant();

            try
            {
                if (client is BinanceRestClient binance)
                {
                    var side = normalizedSide == "BUY" ? OrderSide.Buy : OrderSide.Sell;

                    if (useFutures)
                    {
                        var result = await binance.UsdFuturesApi.Trading.PlaceOrderAsync(
                            symbol: symbol,
                            side: side,
                            type: string.Equals(orderType, "Market", StringComparison.OrdinalIgnoreCase)
                                ? FuturesOrderType.Market
                                : FuturesOrderType.Limit,
                            quantity: quantity,
                            price: entryPrice,
                            stopPrice: stopLoss);

                        return result.Success
                            ? BrokerOrderResult.SuccessResult(result.Data?.Id.ToString(), result.Data?.Price)
                            : BrokerOrderResult.Fail("Exchange rejected the order.");
                    }

                    var spot = await binance.SpotApi.Trading.PlaceOrderAsync(
                        symbol: symbol,
                        side: side,
                        type: string.Equals(orderType, "Market", StringComparison.OrdinalIgnoreCase)
                            ? SpotOrderType.Market
                            : SpotOrderType.Limit,
                        quantity: quantity,
                        price: entryPrice);

                   
                        return spot.Success
    ? BrokerOrderResult.SuccessResult(spot.Data?.Id.ToString(), spot.Data?.Price)
    : BrokerOrderResult.Fail("Exchange rejected the order.");
                }

                if (client is BybitRestClient bybit)
                {
                    var side = normalizedSide == "BUY"
                        ? Bybit.Net.Enums.OrderSide.Buy
                        : Bybit.Net.Enums.OrderSide.Sell;
                    var category = useFutures
                        ? Bybit.Net.Enums.Category.Linear
                        : Bybit.Net.Enums.Category.Spot;

                    var result = await bybit.V5Api.Trading.PlaceOrderAsync(
                        category: category,
                        symbol: symbol,
                        side: side,
                        type: string.Equals(orderType, "Market", StringComparison.OrdinalIgnoreCase)
                            ? Bybit.Net.Enums.NewOrderType.Market
                            : Bybit.Net.Enums.NewOrderType.Limit,
                        quantity: quantity,
                        price: entryPrice,
                        stopLoss: stopLoss,
                        takeProfit: takeProfit);

                    return result.Success
                        ? BrokerOrderResult.SuccessResult(result.Data?.OrderId, null)
                        : BrokerOrderResult.Fail("Exchange rejected the order.");
                }
            }
            catch
            {
                // Exchange-specific errors are intentionally not exposed to callers.
            }

            return BrokerOrderResult.Fail("Order could not be submitted.");
        }

        public async Task<bool> CancelOrderAsync(int userId, string symbol, string orderId, bool useFutures = false)
        {
            var connection = await GetConnectionAsync(userId);
            if (connection == null || !connection.IsConnected)
                return false;

            try
            {
                var client = CreateClient(connection);
                if (client is BinanceRestClient binance && long.TryParse(orderId, out var numericOrderId))
                {
                    if (useFutures)
                        return (await binance.UsdFuturesApi.Trading.CancelOrderAsync(symbol, numericOrderId)).Success;

                    return (await binance.SpotApi.Trading.CancelOrderAsync(symbol, numericOrderId)).Success;
                }

                if (client is BybitRestClient bybit)
                {
                    var category = useFutures ? Bybit.Net.Enums.Category.Linear : Bybit.Net.Enums.Category.Spot;
                    return (await bybit.V5Api.Trading.CancelOrderAsync(category, symbol, orderId)).Success;
                }
            }
            catch { }

            return false;
        }

        public async Task<object?> GetOpenOrdersAsync(int userId, string? symbol = null, bool useFutures = false)
        {
            var connection = await GetConnectionAsync(userId);
            if (connection == null || !connection.IsConnected)
                return null;

            try
            {
                var client = CreateClient(connection);
                if (client is BinanceRestClient binance)
                {
                    if (useFutures)
                    {
                        var result = await binance.UsdFuturesApi.Trading.GetOpenOrdersAsync(symbol);
                        return result.Success ? result.Data : null;
                    }

                    var spot = await binance.SpotApi.Trading.GetOpenOrdersAsync(symbol);
                    return spot.Success ? spot.Data : null;
                }

                if (client is BybitRestClient bybit)
                {
                    var category = useFutures ? Bybit.Net.Enums.Category.Linear : Bybit.Net.Enums.Category.Spot;
                    var result = await bybit.V5Api.Trading.GetOrdersAsync(category, symbol);
                    return result.Success ? result.Data : null;
                }
            }
            catch { }

            return null;
        }

        private async Task<BrokerConnection?> GetConnectionAsync(int userId)
        {
            return await _context.BrokerConnections
                .FirstOrDefaultAsync(b => b.UserId == userId && b.IsActive && b.IsConnected);
        }

        private object CreateClient(BrokerConnection connection)
        {
            var apiKey = _protector.Unprotect(connection.ApiKey);
            var apiSecret = string.IsNullOrWhiteSpace(connection.ApiSecret)
                ? null
                : _protector.Unprotect(connection.ApiSecret);

            if (string.IsNullOrWhiteSpace(apiSecret))
                throw new InvalidOperationException("Broker secret is missing.");

            if (connection.BrokerName.Equals("binance", StringComparison.OrdinalIgnoreCase))
            {
                var client = new BinanceRestClient(options =>
                {
                    options.Environment = connection.IsTestnet
                        ? BinanceEnvironment.Testnet
                        : BinanceEnvironment.Live;
                });
                client.SetApiCredentials(new ApiCredentials(apiKey, apiSecret));
                return client;
            }

            if (connection.BrokerName.Equals("bybit", StringComparison.OrdinalIgnoreCase))
            {
                var client = new BybitRestClient(options =>
                {
                    options.Environment = connection.IsTestnet
                        ? BybitEnvironment.Testnet
                        : BybitEnvironment.Live;
                });
                client.SetApiCredentials(new ApiCredentials(apiKey, apiSecret));
                return client;
            }

            throw new InvalidOperationException("Unsupported broker.");
        }

        private static async Task<bool> VerifyConnectionAsync(object client, BrokerConnection connection)
        {
            if (client is BinanceRestClient binance)
            {
                var result = await binance.SpotApi.Account.GetAccountInfoAsync();
                return result.Success;
            }

            if (client is BybitRestClient bybit)
            {
                var result = await bybit.V5Api.Account.GetMarginAccountInfoAsync(CancellationToken.None);
                return result.Success;
            }

            return false;
        }
    }

    public sealed class BrokerConnectionStatus
    {
        public int Id { get; set; }
        public string BrokerName { get; set; } = string.Empty;
        public bool IsConnected { get; set; }
        public bool IsTestnet { get; set; }
        public bool IsLiveTradingEnabled { get; set; }
        public DateTime? LastConnectedAt { get; set; }
        public DateTime? LastDisconnectedAt { get; set; }
    }

    public sealed class BrokerOrderResult
    {
        public bool Success { get; init; }
        public string? OrderId { get; init; }
        public decimal? Price { get; init; }
        public string? Error { get; init; }

        public static BrokerOrderResult SuccessResult(string? orderId, decimal? price) =>
            new() { Success = true, OrderId = orderId, Price = price };

        public static BrokerOrderResult Fail(string error) =>
            new() { Success = false, Error = error };
    }
}
