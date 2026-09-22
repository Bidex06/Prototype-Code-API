using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Bybit.Net;
using Bybit.Net.Clients;
using CryptoExchange.Net.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
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
        private readonly ExchangeMarketDataService _marketDataService;

        public BrokerService(
            ApplicationDbContext context,
            ICredentialProtector protector,
            ExchangeMarketDataService? marketDataService = null)
        {
            _context = context;
            _protector = protector;
            _marketDataService = marketDataService
                ?? new ExchangeMarketDataService(
                    new MemoryCache(new MemoryCacheOptions()));
        }

        public async Task<bool> ConnectAsync(
            int userId,
            BrokerConnection connection)
        {
            var brokerName = connection.BrokerName?.Trim();
            var apiKey = connection.ApiKey?.Trim();
            var apiSecret = connection.ApiSecret?.Trim();

            if (string.IsNullOrWhiteSpace(brokerName) ||
                (
                    !brokerName.Equals("Binance", StringComparison.OrdinalIgnoreCase) &&
                    !brokerName.Equals("Bybit", StringComparison.OrdinalIgnoreCase)
                ) ||
                string.IsNullOrWhiteSpace(apiKey) ||
                string.IsNullOrWhiteSpace(apiSecret))
            {
                return false;
            }

            brokerName = brokerName.Equals("Binance", StringComparison.OrdinalIgnoreCase)
                ? "Binance"
                : "Bybit";

            var persisted = await _context.BrokerConnections
                .FirstOrDefaultAsync(b =>
                    b.UserId == userId &&
                    b.BrokerName == brokerName);

            if (persisted == null)
            {
                persisted = new BrokerConnection
                {
                    UserId = userId,
                    BrokerName = brokerName,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };

                _context.BrokerConnections.Add(persisted);
            }

            // A connect/reconnect request is a fresh credential verification.
            // Always replace the encrypted credentials, reactivate the record,
            // and require a fresh live-trading opt-in. This prevents stale or
            // previously approved live credentials from silently carrying over.
            persisted.ApiKey = _protector.Protect(apiKey);
            persisted.ApiSecret = _protector.Protect(apiSecret);
            persisted.IsActive = true;
            persisted.IsConnected = false;
            persisted.IsTestnet = true;
            persisted.IsLiveTradingEnabled = false;
            persisted.LiveTradingOptInAt = null;
            persisted.LastDisconnectedAt = DateTime.UtcNow;
            persisted.UpdatedAt = DateTime.UtcNow;

            try
            {
                var client = CreateClient(persisted);

                var connected =
                    await VerifyConnectionWithRetryAsync(client, persisted);

                persisted.IsConnected = connected;
                persisted.IsActive = connected || persisted.IsActive;
                persisted.LastConnectedAt =
                    connected
                        ? DateTime.UtcNow
                        : persisted.LastConnectedAt;

                persisted.UpdatedAt = DateTime.UtcNow;

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = connected
                        ? "BrokerConnected"
                        : "BrokerConnectionFailed",

                    Details =
                        $"Broker {persisted.BrokerName} connection attempt " +
                        $"completed in {(persisted.IsTestnet ? "testnet" : "live")} mode.",

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

        public async Task<bool> DisconnectAsync(
            int userId,
            int? connectionId = null)
        {
            var connections = await _context.BrokerConnections
                .Where(b =>
                    b.UserId == userId &&
                    b.IsActive &&
                    (!connectionId.HasValue ||
                     b.Id == connectionId.Value))
                .ToListAsync();

            if (connections.Count == 0)
                return false;

            foreach (var connection in connections)
            {
                connection.IsConnected = false;
                connection.LastDisconnectedAt = DateTime.UtcNow;
                connection.UpdatedAt = DateTime.UtcNow;

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "BrokerDisconnected",
                    Details = $"Broker {connection.BrokerName} connection {connection.Id} disconnected.",
                    CreatedAt = DateTime.UtcNow
                });
            }

            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> IsConnectedAsync(
            int userId,
            string? brokerName = null)
        {
            return await _context.BrokerConnections.AnyAsync(b =>
                b.UserId == userId &&
                b.IsActive &&
                b.IsConnected &&
                (brokerName == null ||
                 b.BrokerName == brokerName));
        }

        public async Task<BrokerConnectionStatus?>
            GetConnectionStatusAsync(int userId, int connectionId)
        {
            return await _context.BrokerConnections
                .AsNoTracking()
                .Where(b => b.UserId == userId && b.IsActive && b.Id == connectionId)
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
                .FirstOrDefaultAsync();
        }

        public async Task<IReadOnlyList<BrokerConnectionStatus>>
            GetConnectionsAsync(int userId)
        {
            return await _context.BrokerConnections
                .AsNoTracking()
                .Where(b =>
                    b.UserId == userId &&
                    b.IsActive)
                .Select(b => new BrokerConnectionStatus
                {
                    Id = b.Id,
                    BrokerName = b.BrokerName,
                    IsConnected = b.IsConnected,
                    IsTestnet = b.IsTestnet,
                    IsLiveTradingEnabled =
                        b.IsLiveTradingEnabled,
                    LastConnectedAt =
                        b.LastConnectedAt,
                    LastDisconnectedAt =
                        b.LastDisconnectedAt
                })
                .ToListAsync();
        }

        public async Task<bool> SetLiveTradingOptInAsync(
            int userId,
            int connectionId,
            bool enabled)
        {
            var connection =
                await _context.BrokerConnections
                    .FirstOrDefaultAsync(
                        b =>
                            b.Id == connectionId &&
                            b.UserId == userId &&
                            b.IsActive);

            if (connection == null)
                return false;

            if (enabled)
            {
                if (!connection.IsConnected)
                {
                    _context.AuditLogs.Add(new AuditLog
                    {
                        UserId = userId,
                        Action = "LiveTradingEnableBlocked",

                        Details =
                            $"Live trading enable attempt blocked for " +
                            $"broker connection {connection.Id}: " +
                            $"connection is not currently connected/verified.",

                        CreatedAt = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync();

                    return false;
                }

                // Re-verify the same stored credentials against the live
                // environment before switching the persisted connection.
                var originalIsTestnet = connection.IsTestnet;
                connection.IsTestnet = false;

                try
                {
                    var liveClient = CreateClient(connection);
                    var verifiedLive = await VerifyConnectionWithRetryAsync(liveClient, connection);

                    if (!verifiedLive)
                    {
                        connection.IsTestnet = originalIsTestnet;
                        _context.AuditLogs.Add(new AuditLog
                        {
                            UserId = userId,
                            Action = "LiveTradingEnableBlocked",
                            Details = $"Live trading enable attempt blocked for broker connection {connection.Id}: live credentials could not be verified.",
                            CreatedAt = DateTime.UtcNow
                        });
                        await _context.SaveChangesAsync();
                        return false;
                    }
                }
                catch
                {
                    connection.IsTestnet = originalIsTestnet;
                    _context.AuditLogs.Add(new AuditLog
                    {
                        UserId = userId,
                        Action = "LiveTradingEnableBlocked",
                        Details = $"Live trading enable attempt blocked for broker connection {connection.Id}: live credential verification failed.",
                        CreatedAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                    return false;
                }

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

                Action = enabled
                    ? "LiveTradingEnabled"
                    : "LiveTradingDisabled",

                Details =
                    $"Broker connection {connection.Id} live-trading " +
                    $"state changed to {enabled}.",

                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();

            return true;
        }

        public sealed record SpotMarketPriceSnapshot(
            string BrokerName,
            bool IsTestnet,
            string Symbol,
            decimal LastPrice,
            decimal SuggestedBuyPrice);

        public async Task<SpotMarketPriceSnapshot?> GetSpotMarketPriceAsync(
            int userId,
            string symbol,
            int? connectionId = null)
        {
            symbol = symbol?.Trim().ToUpperInvariant() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(symbol))
                return null;

            var connection = await GetConnectionAsync(userId, connectionId);
            if (connection == null || !connection.IsActive || !connection.IsConnected)
                return null;

            try
            {
                var client = CreateClient(connection);
                decimal lastPrice;

                if (client is BinanceRestClient binance)
                {
                    var ticker = await binance.SpotApi.ExchangeData
                        .GetPriceAsync(symbol, CancellationToken.None);

                    if (!ticker.Success || ticker.Data == null || ticker.Data.Price <= 0m)
                        return null;

                    lastPrice = ticker.Data.Price;
                }
                else if (client is BybitRestClient bybit)
                {
                    var ticker = await bybit.V5Api.ExchangeData
                        .GetSpotTickersAsync(symbol);

                    var tickerData = ticker.Success
                        ? ticker.Data.List.FirstOrDefault()
                        : null;

                    if (tickerData == null || tickerData.LastPrice <= 0m)
                        return null;

                    lastPrice = tickerData.LastPrice;
                }
                else
                {
                    return null;
                }

                var suggestedBuyPrice = Math.Round(
                    lastPrice * 1.01m,
                    2,
                    MidpointRounding.AwayFromZero);

                return new SpotMarketPriceSnapshot(
                    connection.BrokerName,
                    connection.IsTestnet,
                    symbol,
                    lastPrice,
                    suggestedBuyPrice);
            }
            catch
            {
                return null;
            }
        }

        public virtual async Task<decimal> GetBalanceAsync(
            int userId,
            string currency = "USDT",
            bool useFutures = false,
            int? connectionId = null)
        {
            var connection = await GetConnectionAsync(userId, connectionId);

            if (connection == null)
                return 0;

            try
            {
                var client = CreateClient(connection);

                if (client is BinanceRestClient binance)
                {
                    if (useFutures)
                    {
                        var result =
                            await binance.UsdFuturesApi
                                .Account
                                .GetAccountInfoAsync();

                        if (result.Success)
                        {
                            return result.Data.Assets
                                .FirstOrDefault(a =>
                                    a.Asset == currency)
                                ?.WalletBalance ?? 0;
                        }
                    }
                    else
                    {
                        var result =
                            await binance.SpotApi
                                .Account
                                .GetAccountInfoAsync();

                        if (result.Success)
                        {
                            return result.Data.Balances
                                .FirstOrDefault(a =>
                                    a.Asset == currency)
                                ?.Available ?? 0;
                        }
                    }
                }

                if (client is BybitRestClient bybit)
                {
                    var accountType = useFutures
                        ? Bybit.Net.Enums.AccountType.Contract
                        : Bybit.Net.Enums.AccountType.Spot;

                    var result =
                        await bybit.V5Api.Account
                            .GetBalancesAsync(
                                accountType,
                                currency,
                                CancellationToken.None);

                    if (result.Success)
                    {
                        return result.Data.List?
                            .FirstOrDefault()?
                            .Assets?
                            .FirstOrDefault(a =>
                                a.Asset == currency)
                            ?.Free ?? 0;
                    }
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
            string orderType = "Market",
            decimal? stopLoss = null,
            decimal? takeProfit = null,
            decimal? entryPrice = null,
            bool useFutures = false,
            string? idempotencyKey = null,
            int? connectionId = null)
        {
            symbol = symbol?.Trim() ?? string.Empty;
            direction = direction?.Trim() ?? string.Empty;
            orderType = orderType?.Trim() ?? string.Empty;

            // 4.8: caller does not provide quantity.
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Invalid order parameters.",
                    $"Order validation failed for symbol '{symbol}'.");
            }

            if (!string.Equals(
                    direction,
                    "BUY",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    direction,
                    "SELL",
                    StringComparison.OrdinalIgnoreCase))
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Invalid order side.",
                    $"Order validation failed because side '{direction}' is invalid.");
            }

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "An idempotency key is required for every order.",
                    $"Order request was rejected because no idempotency key was supplied.");
            }

            idempotencyKey = idempotencyKey.Trim();

            if (idempotencyKey.Length > 100)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Invalid idempotency key.",
                    $"Order request was rejected because the idempotency key exceeds the allowed length.");
            }

            if (!string.Equals(
                    orderType,
                    "Market",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(
                    orderType,
                    "Limit",
                    StringComparison.OrdinalIgnoreCase))
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Invalid order type.",
                    $"Order type '{orderType}' is not supported.");
            }

            if (string.Equals(
                    orderType,
                    "Limit",
                    StringComparison.OrdinalIgnoreCase) &&
                (!entryPrice.HasValue ||
                 entryPrice.Value <= 0))
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "A valid entry price is required for limit orders.",
                    $"Limit order for '{symbol}' was rejected because the entry price is missing or invalid.");
            }

            var connection =
                await GetConnectionAsync(userId, connectionId);

            if (connection == null ||
                !connection.IsConnected)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderBlocked_NoBrokerConnection",
                    "No active broker connection.",
                    $"Order for '{symbol}' was blocked because no active broker connection exists.");
            }

            if (!connection.IsTestnet &&
                !connection.IsLiveTradingEnabled)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderBlocked_LiveTradingNotEnabled",
                    "Live trading is not explicitly enabled for this connection.",
                    $"Order for '{symbol}' was blocked because explicit live-trading opt-in is not enabled.");
            }

            // Spot SELL is a position-reducing action, not a short entry.
            // Protective SL/TP belongs on the asset-holding side of a spot
            // position, so a spot SELL with protection parameters is rejected
            // rather than silently changing the meaning of the request.
            if (!useFutures &&
                direction.Equals("SELL", StringComparison.OrdinalIgnoreCase) &&
                (stopLoss.HasValue || takeProfit.HasValue))
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Spot SELL orders cannot open short positions with protective SL/TP.",
                    $"Spot SELL for '{symbol}' was rejected because spot SELL closes/reduces an existing asset position. Protection is created for spot BUY holdings instead.");
            }

            if (connection.BrokerName.Equals("Bybit", StringComparison.OrdinalIgnoreCase) &&
                !useFutures &&
                (stopLoss.HasValue || takeProfit.HasValue) &&
                orderType.Equals("Market", StringComparison.OrdinalIgnoreCase))
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Protected Bybit Spot orders must use a limit entry.",
                    $"Bybit Spot order for '{symbol}' was rejected because the protected Spot path uses a limit entry so TP/SL can be attached safely.");
            }

            // ---------------------------------------------------------
            // IDEMPOTENCY CHECK
            // ---------------------------------------------------------

            var existingTrade =
                await _context.Trades
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t =>
                        t.UserId == userId &&
                        t.IdempotencyKey == idempotencyKey);

            if (existingTrade != null)
            {
                if (!string.IsNullOrWhiteSpace(
                        existingTrade.OrderId))
                {
                    await AuditOrderEventAsync(
                        userId,
                        "OrderIdempotencyReplay",
                        $"Existing order {existingTrade.OrderId} was returned for idempotency key '{idempotencyKey}'.");

                    return BrokerOrderResult.SuccessResult(
                        existingTrade.OrderId,
                        existingTrade.EntryPrice);
                }

                return await AuditOrderFailureAsync(
                    userId,
                    "OrderBlocked_IdempotencyConflict",
                    "An order with this idempotency key is already being processed. Reconciliation is required before retrying.",
                    $"Duplicate order request blocked for idempotency key '{idempotencyKey}'.");
            }

            // ---------------------------------------------------------
            // SERVER-SIDE RISK SIZING
            // ---------------------------------------------------------

            var riskSetting = await _context.RiskSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == userId);

            if (riskSetting == null)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderBlocked_RiskSettingsMissing",
                    "Risk settings are not configured for this user.",
                    $"Order for '{symbol}' was blocked because risk settings are not configured for user {userId}.");
            }

            // ---------------------------------------------------------
            // #8.4 EMERGENCY KILL SWITCH
            // ---------------------------------------------------------
            // Server-side trading circuit breaker. New orders must be
            // blocked before any exchange-dependent execution occurs.
            if (riskSetting.IsEmergencyKillSwitch)
            {
                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "RiskBlocked_EmergencyKillSwitch",
                    Details =
                        "Order blocked because the emergency kill switch is active." +
                        (riskSetting.EmergencyKillSwitchActivatedAt.HasValue
                            ? $" Activated at {riskSetting.EmergencyKillSwitchActivatedAt.Value:O}."
                            : string.Empty),
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return BrokerOrderResult.Fail(
                    "Trading is blocked because the emergency kill switch is active.");
            }

            if (riskSetting.RiskPerTrade <= 0m ||
                riskSetting.RiskPerTrade > 100m)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderBlocked_InvalidRiskConfiguration",
                    "Configured risk per trade is invalid.",
                    $"Order for '{symbol}' was blocked because RiskPerTrade ({riskSetting.RiskPerTrade:F2}%) is invalid.");
            }

            // ---------------------------------------------------------
            // #8.1 MAXIMUM OPEN TRADES
            // ---------------------------------------------------------
            // Count every trade that can still represent an open or
            // unresolved exchange exposure. Pending and reconciliation
            // states are included so a user cannot bypass the limit by
            // submitting several requests before reconciliation runs.
            if (riskSetting.MaxOpenTrades <= 0)
            {
                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "RiskBlocked_MaxOpenTrades",
                    Details =
                        "Order blocked because MaxOpenTrades is not configured " +
                        "to a positive value.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return BrokerOrderResult.Fail(
                    "Maximum open trades is not configured correctly.");
            }

            var openTradeCount = await _context.Trades
                .CountAsync(t =>
                    t.UserId == userId &&
                    (t.Status == "Pending" ||
                     t.Status == "Open" ||
                     t.Status == "New" ||
                     t.Status == "PartiallyFilled" ||
                     t.Status == "Filled" ||
                     t.Status == "PendingReconciliation"));

            if (openTradeCount >= riskSetting.MaxOpenTrades)
            {
                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "RiskBlocked_MaxOpenTrades",
                    Details =
                        $"Order blocked because the maximum number of open " +
                        $"trades ({riskSetting.MaxOpenTrades}) has been reached. " +
                        $"Current active/unresolved trades: {openTradeCount}.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return BrokerOrderResult.Fail(
                    $"Maximum open trades limit reached ({riskSetting.MaxOpenTrades}).");
            }

            // ---------------------------------------------------------
            // #8.2 DAILY-LOSS LIMIT
            // ---------------------------------------------------------
            // DailyLossLimit is configured as a percentage of the
            // current account balance. Only realized losses from trades
            // closed today are counted. The check happens before any
            // exchange order submission.
            if (riskSetting.DailyLossLimit <= 0m ||
                riskSetting.DailyLossLimit > 100m)
            {
                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "RiskBlocked_DailyLossLimit",
                    Details =
                        "Order blocked because DailyLossLimit is not configured " +
                        "as a valid percentage between 0 and 100.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return BrokerOrderResult.Fail(
                    "Daily loss limit is not configured correctly.");
            }

            var balance = await GetBalanceAsync(
                userId,
                "USDT",
                useFutures,
                connection.Id);

            if (balance <= 0m)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderBlocked_InsufficientBalance",
                    "Insufficient available balance.",
                    $"Order for '{symbol}' was blocked because available USDT balance was insufficient.");
            }

            var utcToday = DateTime.UtcNow.Date;
            var tomorrowUtc = utcToday.AddDays(1);

            var dailyRealizedLoss = await _context.Trades
                .Where(t =>
                    t.UserId == userId &&
                    t.ExitTime.HasValue &&
                    t.ExitTime.Value >= utcToday &&
                    t.ExitTime.Value < tomorrowUtc &&
                    t.ProfitLoss < 0m)
                .SumAsync(t => (decimal?)t.ProfitLoss) ?? 0m;

            dailyRealizedLoss = Math.Abs(dailyRealizedLoss);

            var dailyLossLimitAmount =
                balance * (riskSetting.DailyLossLimit / 100m);

            if (dailyRealizedLoss >= dailyLossLimitAmount)
            {
                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "RiskBlocked_DailyLossLimit",
                    Details =
                        $"Order blocked because today's realized loss " +
                        $"({dailyRealizedLoss:F2} USDT) has reached or exceeded " +
                        $"the daily loss limit ({dailyLossLimitAmount:F2} USDT, " +
                        $"{riskSetting.DailyLossLimit:F2}%).",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return BrokerOrderResult.Fail(
                    $"Daily loss limit reached ({dailyLossLimitAmount:F2} USDT).");
            }

            // ---------------------------------------------------------
            // #8.3 MAXIMUM DRAWDOWN
            // ---------------------------------------------------------
            // Track the user's peak account equity and block new orders
            // once the configured percentage drawdown is reached.
            if (riskSetting.MaxDrawdown <= 0m ||
                riskSetting.MaxDrawdown > 100m)
            {
                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "RiskBlocked_MaxDrawdown",
                    Details =
                        "Order blocked because MaxDrawdown is not configured " +
                        "as a valid percentage between 0 and 100.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return BrokerOrderResult.Fail(
                    "Maximum drawdown is not configured correctly.");
            }

            var currentEquity = balance;

            if (useFutures)
            {
                var unrealizedPnL = await _context.Positions
                    .Where(p =>
                        p.UserId == userId &&
                        p.Status == "Open")
                    .SumAsync(p => (decimal?)p.UnrealizedPnL) ?? 0m;

                currentEquity += unrealizedPnL;
            }

            if (currentEquity <= 0m)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderBlocked_EquityUnavailable",
                    "Current account equity is not available.",
                    $"Order for '{symbol}' was blocked because current account equity could not be established.");
            }

            // Reload the tracked RiskSetting so the high-water mark can
            // be safely persisted.
            var trackedRiskSetting = await _context.RiskSettings
                .FirstOrDefaultAsync(r => r.UserId == userId);

            if (trackedRiskSetting == null)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderBlocked_RiskSettingsMissing",
                    "Risk settings are not configured for this user.",
                    $"Order for '{symbol}' was blocked because risk settings are not configured for user {userId}.");
            }

            if (trackedRiskSetting.EquityHighWaterMark <= 0m)
            {
                trackedRiskSetting.EquityHighWaterMark = currentEquity;
                trackedRiskSetting.EquityHighWaterMarkUpdatedAt =
                    DateTime.UtcNow;

                await _context.SaveChangesAsync();
            }
            else if (currentEquity > trackedRiskSetting.EquityHighWaterMark)
            {
                trackedRiskSetting.EquityHighWaterMark = currentEquity;
                trackedRiskSetting.EquityHighWaterMarkUpdatedAt =
                    DateTime.UtcNow;

                await _context.SaveChangesAsync();
            }

            var equityHighWaterMark =
                trackedRiskSetting.EquityHighWaterMark;

            var drawdownPercentage =
                ((equityHighWaterMark - currentEquity) /
                 equityHighWaterMark) * 100m;

            if (drawdownPercentage >= trackedRiskSetting.MaxDrawdown)
            {
                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "RiskBlocked_MaxDrawdown",
                    Details =
                        $"Order blocked because current equity " +
                        $"({currentEquity:F2} USDT) is down " +
                        $"{drawdownPercentage:F2}% from the equity high-water " +
                        $"mark ({equityHighWaterMark:F2} USDT), reaching the " +
                        $"configured maximum drawdown of " +
                        $"{trackedRiskSetting.MaxDrawdown:F2}%.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return BrokerOrderResult.Fail(
                    $"Maximum drawdown limit reached " +
                    $"({trackedRiskSetting.MaxDrawdown:F2}%).");
            }

            ExchangeOrderRules rules;

            if (string.Equals(
                    connection.BrokerName,
                    "Binance",
                    StringComparison.OrdinalIgnoreCase))
            {
                rules = await _marketDataService.GetBinanceRulesAsync(
                    symbol,
                    useFutures,
                    connection.IsTestnet);
            }
            else if (string.Equals(
                         connection.BrokerName,
                         "Bybit",
                         StringComparison.OrdinalIgnoreCase))
            {
                rules = await _marketDataService.GetBybitRulesAsync(
                    symbol,
                    useFutures,
                    connection.IsTestnet);
            }
            else
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Unsupported broker.",
                    $"Order for '{symbol}' was rejected because broker '{connection.BrokerName}' is unsupported.");
            }

            decimal sizingPrice;

            if (entryPrice.HasValue && entryPrice.Value > 0m)
            {
                sizingPrice = entryPrice.Value;
            }
            else
            {
                // A MARKET order still needs a real exchange price for
                // server-side risk sizing. Do not use a parameterless
                // ExchangeMarketDataService here because it does not know
                // the persisted testnet/live environment of this user's
                // broker connection. Query the same authenticated client
                // created from that connection instead.
                var pricingClient = CreateClient(connection);

                if (pricingClient is BinanceRestClient pricingBinance)
                {
                    if (useFutures)
                    {
                        var ticker = await pricingBinance.UsdFuturesApi
                            .ExchangeData
                            .GetTickerAsync(symbol, CancellationToken.None);

                        sizingPrice = ticker.Success && ticker.Data != null
                            ? ticker.Data.LastPrice
                            : 0m;
                    }
                    else
                    {
                        var ticker = await pricingBinance.SpotApi
                            .ExchangeData
                            .GetPriceAsync(symbol, CancellationToken.None);

                        sizingPrice = ticker.Success && ticker.Data != null
                            ? ticker.Data.Price
                            : 0m;
                    }
                }
                else
                {
                    sizingPrice = rules.ReferencePrice;
                }
            }

            if (sizingPrice <= 0m)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Unable to determine a valid pricing reference.",
                    $"Order for '{symbol}' was rejected because no valid pricing reference was available.");
            }

            var leverage = useFutures
                ? riskSetting.Leverage
                : 1m;

            if (leverage <= 0m)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Configured leverage is invalid.",
                    $"Order for '{symbol}' was rejected because configured leverage ({leverage:F2}) is invalid.");
            }

            if (useFutures && rules.MaxLeverage > 0m)
            {
                leverage = Math.Min(
                    leverage,
                    rules.MaxLeverage);
            }
            else if (!useFutures)
            {
                leverage = 1m;
            }

            var riskCapital =
                balance * (riskSetting.RiskPerTrade / 100m);

            var targetNotional =
                riskCapital * leverage;

            var calculatedQuantity =
                targetNotional / sizingPrice;

            // ---------------------------------------------------------
            // 4.9 FINAL QUANTITY VALIDATION
            // ---------------------------------------------------------

            if (calculatedQuantity <= 0m)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Calculated order quantity is invalid.",
                    $"Order for '{symbol}' was rejected because the server-calculated quantity is invalid.");
            }

            if (rules.QuantityStep <= 0m)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Exchange quantity step is invalid.",
                    $"Order for '{symbol}' was rejected because the exchange quantity step is invalid.");
            }

            // Normalize DOWN only.
            // Never round up because doing so could increase risk.
            calculatedQuantity =
                Math.Floor(
                    calculatedQuantity / rules.QuantityStep)
                * rules.QuantityStep;

            if (calculatedQuantity <= 0m)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Calculated order quantity became zero after exchange rounding.",
                    $"Order for '{symbol}' was rejected because quantity became zero after exchange rounding.");
            }

            if (calculatedQuantity < rules.MinQuantity)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Calculated quantity is below the exchange minimum.",
                    $"Order for '{symbol}' was rejected because calculated quantity is below the exchange minimum.");
            }

            if (rules.MaxQuantity > 0m &&
                calculatedQuantity > rules.MaxQuantity)
            {
                calculatedQuantity =
                    Math.Floor(
                        rules.MaxQuantity / rules.QuantityStep)
                    * rules.QuantityStep;
            }

            // Re-check minimum after maximum normalization.
            if (calculatedQuantity < rules.MinQuantity)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Exchange quantity limits cannot satisfy the calculated order size.",
                    $"Order for '{symbol}' was rejected because exchange quantity limits cannot satisfy the calculated order size.");
            }

            var finalNotional =
                calculatedQuantity * sizingPrice;

            if (finalNotional <= 0m)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Final order value is invalid.",
                    $"Order for '{symbol}' was rejected because final order value is invalid.");
            }

            if (rules.MinNotional > 0m &&
                finalNotional < rules.MinNotional)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderValidationFailed",
                    "Final order value is below the exchange minimum.",
                    $"Order for '{symbol}' was rejected because final order value is below the exchange minimum.");
            }

            // Spot orders must never exceed the available quote balance.
            if (!useFutures &&
                finalNotional > balance)
            {
                return await AuditOrderFailureAsync(
                    userId,
                    "OrderBlocked_InsufficientBalance",
                    "Final order value exceeds available balance.",
                    $"Order for '{symbol}' was blocked because final order value exceeds available balance.");
            }

            // Server-authoritative quantity.
            var quantity = calculatedQuantity;

            if (!useFutures &&
                direction.Equals("SELL", StringComparison.OrdinalIgnoreCase))
            {
                var baseAsset = ExtractBaseAsset(symbol);
                var availableBase = await GetSpotAssetBalanceAsync(
                    baseAsset,
                    connection);

                if (availableBase <= 0m)
                {
                    return await AuditOrderFailureAsync(
                        userId,
                        "OrderBlocked_InsufficientAssetBalance",
                        "Insufficient available base-asset balance for spot SELL.",
                        $"Spot SELL for '{symbol}' was blocked because no sellable {baseAsset} balance was available.");
                }

                quantity = Math.Min(quantity, availableBase);
                quantity = Math.Floor(quantity / rules.QuantityStep) * rules.QuantityStep;

                if (quantity < rules.MinQuantity)
                {
                    return await AuditOrderFailureAsync(
                        userId,
                        "OrderValidationFailed",
                        "Available base-asset balance is below the exchange minimum sell quantity.",
                        $"Spot SELL for '{symbol}' was rejected because available {baseAsset} balance is below the exchange minimum quantity.");
                }
            }

            // ---------------------------------------------------------
            // RESERVE IDEMPOTENCY KEY BEFORE EXCHANGE CALL
            // ---------------------------------------------------------

            var pendingTrade = new Trade
            {
                UserId = userId,
                Symbol = symbol,
                Direction = direction.ToUpperInvariant(),

                Quantity = quantity,

                EntryPrice =
                    entryPrice ?? 0m,

                StopLoss = stopLoss,
                TakeProfit = takeProfit,
                ProtectionStatus = (stopLoss.HasValue || takeProfit.HasValue) ? "Pending" : "NotRequired",

                EntryTime = DateTime.UtcNow,

                Status = "Pending",

                BrokerName = connection.BrokerName,

                IdempotencyKey = idempotencyKey,

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Trades.Add(pendingTrade);

            try
            {
                await _context.SaveChangesAsync();

                await AuditOrderEventAsync(
                    userId,
                    "OrderReserved",
                    $"Order reserved before exchange submission. Symbol: {symbol}, Side: {direction.ToUpperInvariant()}, Quantity: {quantity}, Broker: {connection.BrokerName}, IdempotencyKey: {idempotencyKey}.");
            }
            catch (DbUpdateException)
            {
                var concurrentTrade =
                    await _context.Trades
                        .AsNoTracking()
                        .FirstOrDefaultAsync(t =>
                            t.UserId == userId &&
                            t.IdempotencyKey == idempotencyKey);

                if (concurrentTrade != null)
                {
                    if (!string.IsNullOrWhiteSpace(
                            concurrentTrade.OrderId))
                    {
                        await AuditOrderEventAsync(
                            userId,
                            "OrderIdempotencyReplay",
                            $"Existing order {concurrentTrade.OrderId} was returned after a concurrent reservation for idempotency key '{idempotencyKey}'.");

                        return BrokerOrderResult.SuccessResult(
                            concurrentTrade.OrderId,
                            concurrentTrade.EntryPrice);
                    }

                    _context.Entry(pendingTrade).State = EntityState.Detached;

                    return await AuditOrderFailureAsync(
                        userId,
                        "OrderBlocked_IdempotencyConflict",
                        "An order with this idempotency key is already being processed. Reconciliation is required before retrying.",
                        $"Concurrent duplicate order request blocked for idempotency key '{idempotencyKey}'.");
                }

                return await AuditOrderFailureAsync(
                    userId,
                    "OrderReservationFailed",
                    "The order could not be reserved safely. No exchange order was submitted.",
                    $"Order reservation failed for symbol '{symbol}'. No exchange order was submitted.");
            }

            // ---------------------------------------------------------
            // #8.4 FINAL EMERGENCY KILL SWITCH CHECK
            // ---------------------------------------------------------
            // Re-read persistent state immediately before exchange
            // submission so a kill-switch activation that occurred while
            // the order was being prepared can still block the trade.
            var finalKillSwitchState = await _context.RiskSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == userId);

            if (finalKillSwitchState?.IsEmergencyKillSwitch == true)
            {
                pendingTrade.Status = "Blocked";
                pendingTrade.Reason =
                    "Emergency kill switch was active before exchange submission.";
                pendingTrade.UpdatedAt = DateTime.UtcNow;

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "RiskBlocked_EmergencyKillSwitch",
                    Details =
                        "Pending order blocked immediately before exchange submission " +
                        "because the emergency kill switch is active.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return BrokerOrderResult.Fail(
                    "Trading is blocked because the emergency kill switch is active.");
            }

            // ---------------------------------------------------------
            // SUBMIT ORDER TO EXCHANGE
            // ---------------------------------------------------------

            try
            {
                var client = CreateClient(connection);

                var normalizedSide =
                    direction.ToUpperInvariant();

                // =====================================================
                // BINANCE
                // =====================================================

                if (client is BinanceRestClient binance)
                {
                    var side =
                        normalizedSide == "BUY"
                            ? OrderSide.Buy
                            : OrderSide.Sell;

                    // -------------------------------------------------
                    // BINANCE FUTURES
                    // -------------------------------------------------

                    if (useFutures)
                    {
                        var result =
                            await binance
                                .UsdFuturesApi
                                .Trading
                                .PlaceOrderAsync(
                                    symbol: symbol,
                                    side: side,

                                    type:
                                        string.Equals(
                                            orderType,
                                            "Market",
                                            StringComparison.OrdinalIgnoreCase)
                                            ? FuturesOrderType.Market
                                            : FuturesOrderType.Limit,

                                    quantity: quantity,
                                    price: entryPrice,
                                    stopPrice: stopLoss);

                        if (!result.Success)
                        {
                            var exchangeError = result.Error?.ToString() ??
                                "Unknown exchange rejection.";

                            pendingTrade.Status = "Failed";
                            pendingTrade.UpdatedAt = DateTime.UtcNow;
                            pendingTrade.Reason =
                                $"Exchange rejected the order: {exchangeError}";

                            _context.AuditLogs.Add(new AuditLog
                            {
                                UserId = userId,
                                Action = "OrderRejectedByExchange",
                                Details =
                                    $"Exchange rejected order for {symbol} on {connection.BrokerName}. " +
                                    $"Exchange error: {exchangeError}",
                                CreatedAt = DateTime.UtcNow
                            });

                            await _context.SaveChangesAsync();

                            return BrokerOrderResult.Fail(
                                $"Exchange rejected the order: {exchangeError}");
                        }

                        var orderId =
                            result.Data?.Id.ToString();

                        if (string.IsNullOrWhiteSpace(orderId))
                        {
                            pendingTrade.Status =
                                "PendingReconciliation";

                            pendingTrade.UpdatedAt =
                                DateTime.UtcNow;

                            pendingTrade.Reason =
                                "Exchange accepted the order but " +
                                "did not return an order ID.";

                            _context.AuditLogs.Add(new AuditLog
                            {
                                UserId = userId,
                                Action = "OrderPendingReconciliation",
                                Details = $"Exchange accepted the order for {symbol} on {connection.BrokerName}, but no exchange order ID was returned. Reconciliation required.",
                                CreatedAt = DateTime.UtcNow
                            });

                            await _context.SaveChangesAsync();

                            return BrokerOrderResult.Fail(
                                "The exchange accepted the order but " +
                                "did not return an order ID. " +
                                "Reconciliation is required.");
                        }

                        pendingTrade.OrderId = orderId;

                        pendingTrade.EntryPrice =
                            result.Data?.Price ??
                            entryPrice ??
                            0m;

                        // Exchange acceptance is not the same as a confirmed fill.
                        // Reconciliation will move this trade to the appropriate
                        // filled/partially-filled/open state.
                        pendingTrade.Status = "Pending";
                        pendingTrade.UpdatedAt =
                            DateTime.UtcNow;

                        await PersistExchangeOrderAsync(
                            userId,
                            pendingTrade,
                            connection,
                            orderId,
                            normalizedSide,
                            orderType,
                            useFutures,
                            "Entry",
                            null);

                        if (stopLoss.HasValue || takeProfit.HasValue)
                        {
                            await TryEnsureProtectionAsync(connection, pendingTrade, orderId, normalizedSide);
                        }

                        _context.AuditLogs.Add(new AuditLog
                        {
                            UserId = userId,
                            Action = "OrderSubmitted",

                            Details =
                                $"Order {orderId} submitted to " +
                                $"{connection.BrokerName} in " +
                                $"{(connection.IsTestnet ? "testnet" : "live")} mode.",

                            CreatedAt = DateTime.UtcNow
                        });

                        await _context.SaveChangesAsync();

                        return BrokerOrderResult.SuccessResult(
                            orderId,
                            result.Data?.Price ?? entryPrice);
                    }

                    // -------------------------------------------------
                    // BINANCE SPOT
                    // -------------------------------------------------

                    var isSpotMarketOrder =
                        string.Equals(
                            orderType,
                            "Market",
                            StringComparison.OrdinalIgnoreCase);

                    var spot = isSpotMarketOrder
                        ? await binance
                            .SpotApi
                            .Trading
                            .PlaceOrderAsync(
                                symbol: symbol,
                                side: side,
                                type: SpotOrderType.Market,
                                quantity: quantity)
                        : await binance
                            .SpotApi
                            .Trading
                            .PlaceOrderAsync(
                                symbol: symbol,
                                side: side,
                                type: SpotOrderType.Limit,
                                quantity: quantity,
                                price: entryPrice,
                                timeInForce: TimeInForce.GoodTillCanceled);

                    if (!spot.Success)
                    {
                        var exchangeError = spot.Error?.ToString() ??
                            "Unknown exchange rejection.";

                        pendingTrade.Status = "Failed";
                        pendingTrade.UpdatedAt = DateTime.UtcNow;
                        pendingTrade.Reason =
                            $"Exchange rejected the order: {exchangeError}";

                        _context.AuditLogs.Add(new AuditLog
                        {
                            UserId = userId,
                            Action = "OrderRejectedByExchange",
                            Details =
                                $"Exchange rejected order for {symbol} on {connection.BrokerName}. " +
                                $"Exchange error: {exchangeError}",
                            CreatedAt = DateTime.UtcNow
                        });

                        await _context.SaveChangesAsync();

                        return BrokerOrderResult.Fail(
                            $"Exchange rejected the order: {exchangeError}");
                    }

                    var spotOrderId =
                        spot.Data?.Id.ToString();

                    if (string.IsNullOrWhiteSpace(
                            spotOrderId))
                    {
                        pendingTrade.Status =
                            "PendingReconciliation";

                        pendingTrade.UpdatedAt =
                            DateTime.UtcNow;

                        pendingTrade.Reason =
                            "Exchange accepted the order but " +
                            "did not return an order ID.";

                        _context.AuditLogs.Add(new AuditLog
                        {
                            UserId = userId,
                            Action = "OrderPendingReconciliation",
                            Details = $"Exchange accepted the order for {symbol} on {connection.BrokerName}, but no exchange order ID was returned. Reconciliation required.",
                            CreatedAt = DateTime.UtcNow
                        });

                        await _context.SaveChangesAsync();

                        return BrokerOrderResult.Fail(
                            "The exchange accepted the order but " +
                            "did not return an order ID. " +
                            "Reconciliation is required.");
                    }

                    pendingTrade.OrderId =
                        spotOrderId;

                    pendingTrade.EntryPrice =
                        spot.Data?.Price ??
                        entryPrice ??
                        0m;

                    // Exchange acceptance is not the same as a confirmed fill.
                        // Reconciliation will move this trade to the appropriate
                        // filled/partially-filled/open state.
                        pendingTrade.Status = "Pending";
                    pendingTrade.UpdatedAt =
                        DateTime.UtcNow;

                    await PersistExchangeOrderAsync(
                        userId,
                        pendingTrade,
                        connection,
                        spotOrderId,
                        normalizedSide,
                        orderType,
                        useFutures,
                        "Entry",
                        null);

                    if (stopLoss.HasValue || takeProfit.HasValue)
                    {
                        await TryEnsureProtectionAsync(connection, pendingTrade, spotOrderId, normalizedSide);
                    }

                    _context.AuditLogs.Add(new AuditLog
                    {
                        UserId = userId,
                        Action = "OrderSubmitted",

                        Details =
                            $"Order {spotOrderId} submitted to " +
                            $"{connection.BrokerName} in " +
                            $"{(connection.IsTestnet ? "testnet" : "live")} mode.",

                        CreatedAt = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync();

                    return BrokerOrderResult.SuccessResult(
                        spotOrderId,
                        spot.Data?.Price ?? entryPrice);
                }

                // =====================================================
                // BYBIT
                // =====================================================

                if (client is BybitRestClient bybit)
                {
                    var side =
                        normalizedSide == "BUY"
                            ? Bybit.Net.Enums.OrderSide.Buy
                            : Bybit.Net.Enums.OrderSide.Sell;

                    var category =
                        useFutures
                            ? Bybit.Net.Enums.Category.Linear
                            : Bybit.Net.Enums.Category.Spot;

                    var result =
                        await bybit
                            .V5Api
                            .Trading
                            .PlaceOrderAsync(
                                category: category,
                                symbol: symbol,
                                side: side,

                                type:
                                    string.Equals(
                                        orderType,
                                        "Market",
                                        StringComparison.OrdinalIgnoreCase)
                                        ? Bybit.Net.Enums.NewOrderType.Market
                                        : Bybit.Net.Enums.NewOrderType.Limit,

                                quantity: quantity,
                                price: entryPrice,
                                stopLoss: stopLoss,
                                takeProfit: takeProfit);

                    if (!result.Success)
                    {
                        var exchangeError = result.Error?.ToString() ??
                            "Unknown exchange rejection.";

                        pendingTrade.Status = "Failed";
                        pendingTrade.UpdatedAt = DateTime.UtcNow;
                        pendingTrade.Reason =
                            $"Exchange rejected the order: {exchangeError}";

                        _context.AuditLogs.Add(new AuditLog
                        {
                            UserId = userId,
                            Action = "OrderRejectedByExchange",
                            Details =
                                $"Exchange rejected order for {symbol} on {connection.BrokerName}. " +
                                $"Exchange error: {exchangeError}",
                            CreatedAt = DateTime.UtcNow
                        });

                        await _context.SaveChangesAsync();

                        return BrokerOrderResult.Fail(
                            $"Exchange rejected the order: {exchangeError}");
                    }

                    var bybitOrderId =
                        result.Data?.OrderId;

                    if (string.IsNullOrWhiteSpace(
                            bybitOrderId))
                    {
                        pendingTrade.Status =
                            "PendingReconciliation";

                        pendingTrade.UpdatedAt =
                            DateTime.UtcNow;

                        pendingTrade.Reason =
                            "Exchange accepted the order but " +
                            "did not return an order ID.";

                        _context.AuditLogs.Add(new AuditLog
                        {
                            UserId = userId,
                            Action = "OrderPendingReconciliation",
                            Details = $"Exchange accepted the order for {symbol} on {connection.BrokerName}, but no exchange order ID was returned. Reconciliation required.",
                            CreatedAt = DateTime.UtcNow
                        });

                        await _context.SaveChangesAsync();

                        return BrokerOrderResult.Fail(
                            "The exchange accepted the order but " +
                            "did not return an order ID. " +
                            "Reconciliation is required.");
                    }

                    pendingTrade.OrderId =
                        bybitOrderId;

                    // Exchange acceptance is not the same as a confirmed fill.
                        // Reconciliation will move this trade to the appropriate
                        // filled/partially-filled/open state.
                        pendingTrade.Status = "Pending";
                    pendingTrade.UpdatedAt =
                        DateTime.UtcNow;

                    if (stopLoss.HasValue || takeProfit.HasValue)
                        pendingTrade.ProtectionStatus = "ExchangeManaged";

                    await PersistExchangeOrderAsync(
                        userId,
                        pendingTrade,
                        connection,
                        bybitOrderId,
                        normalizedSide,
                        orderType,
                        useFutures,
                        "Entry",
                        null);

                    _context.AuditLogs.Add(new AuditLog
                    {
                        UserId = userId,
                        Action = "OrderSubmitted",

                        Details =
                            $"Order {bybitOrderId} submitted to " +
                            $"{connection.BrokerName} in " +
                            $"{(connection.IsTestnet ? "testnet" : "live")} mode.",

                        CreatedAt = DateTime.UtcNow
                    });

                    await _context.SaveChangesAsync();

                    return BrokerOrderResult.SuccessResult(
                        bybitOrderId,
                        null);
                }

                pendingTrade.Status = "Failed";
                pendingTrade.UpdatedAt =
                    DateTime.UtcNow;
                pendingTrade.Reason =
                    "Unsupported broker.";

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "OrderValidationFailed",
                    Details = $"Order for '{symbol}' was rejected because broker '{connection.BrokerName}' is unsupported.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();

                return BrokerOrderResult.Fail(
                    "Unsupported broker.");
            }
            catch (Exception ex)
            {
                pendingTrade.Status =
                    "PendingReconciliation";

                pendingTrade.UpdatedAt =
                    DateTime.UtcNow;

                pendingTrade.Reason =
                    "Exchange submission result is unknown; " +
                    "reconciliation is required.";

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "OrderSubmissionUnknown",
                    Details =
                        $"Order submission result is unknown for {symbol} on {connection.BrokerName}. " +
                        $"Exception: {ex.Message} The trade was moved to PendingReconciliation.",
                    CreatedAt = DateTime.UtcNow
                });

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch
                {
                    // Do not expose database or exchange internals.
                }

                return BrokerOrderResult.Fail(
                    "The order submission result is unknown. " +
                    "Reconciliation is required before retrying.");
            }
        }


        internal async Task<bool> TryEnsureProtectionAsync(
            BrokerConnection connection,
            Trade trade,
            string entryExchangeOrderId,
            string entrySide)
        {
            if (!trade.StopLoss.HasValue && !trade.TakeProfit.HasValue)
            {
                trade.ProtectionStatus = "NotRequired";
                trade.UpdatedAt = DateTime.UtcNow;
                return true;
            }

            if (connection.BrokerName.Equals("Bybit", StringComparison.OrdinalIgnoreCase))
            {
                trade.ProtectionStatus = "ExchangeManaged";
                trade.UpdatedAt = DateTime.UtcNow;
                return true;
            }

            if (!connection.BrokerName.Equals("Binance", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!long.TryParse(entryExchangeOrderId, out var numericEntryOrderId))
                return false;

            try
            {
                var client = CreateClient(connection);
                if (client is not BinanceRestClient binance)
                    return false;

                var parent = await _context.ExchangeOrders.FirstOrDefaultAsync(o =>
                    o.TradeId == trade.Id &&
                    o.ExchangeOrderId == entryExchangeOrderId &&
                    o.OrderRole == "Entry");

                if (parent == null)
                    return false;

                if (parent.IsFutures)
                {
                    var entry = await binance.UsdFuturesApi.Trading.GetOrderAsync(
                        trade.Symbol, numericEntryOrderId, null, null, CancellationToken.None);

                    if (!entry.Success || entry.Data == null)
                    {
                        trade.ProtectionStatus = "ProtectionPending";
                        await _context.SaveChangesAsync();
                        return false;
                    }

                    var filledQuantity = Convert.ToDecimal(entry.Data.QuantityFilled, System.Globalization.CultureInfo.InvariantCulture);
                    if (filledQuantity <= 0m)
                    {
                        trade.ProtectionStatus = "Pending";
                        trade.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                        return true;
                    }

                    var protectionSide = entrySide.Equals("BUY", StringComparison.OrdinalIgnoreCase)
                        ? OrderSide.Sell
                        : OrderSide.Buy;

                    if (trade.StopLoss.HasValue && string.IsNullOrWhiteSpace(trade.StopLossOrderId))
                    {
                        var stop = await binance.UsdFuturesApi.Trading.PlaceOrderAsync(
                            symbol: trade.Symbol, side: protectionSide, type: FuturesOrderType.StopMarket,
                            quantity: filledQuantity, positionSide: PositionSide.Both, reduceOnly: true,
                            stopPrice: trade.StopLoss.Value);

                        if (!stop.Success || stop.Data == null)
                        {
                            trade.ProtectionStatus = "ProtectionPending";
                            trade.UpdatedAt = DateTime.UtcNow;
                            await _context.SaveChangesAsync();
                            return false;
                        }

                        trade.StopLossOrderId = stop.Data.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        await PersistExchangeOrderAsync(trade.UserId, trade, connection, trade.StopLossOrderId,
                            protectionSide == OrderSide.Buy ? "BUY" : "SELL", "StopMarket", true, "StopLoss", parent.Id);
                    }

                    if (trade.TakeProfit.HasValue && string.IsNullOrWhiteSpace(trade.TakeProfitOrderId))
                    {
                        var tp = await binance.UsdFuturesApi.Trading.PlaceOrderAsync(
                            symbol: trade.Symbol, side: protectionSide, type: FuturesOrderType.TakeProfitMarket,
                            quantity: filledQuantity, positionSide: PositionSide.Both, reduceOnly: true,
                            stopPrice: trade.TakeProfit.Value);

                        if (!tp.Success || tp.Data == null)
                        {
                            trade.ProtectionStatus = "ProtectionPending";
                            trade.UpdatedAt = DateTime.UtcNow;
                            await _context.SaveChangesAsync();
                            return false;
                        }

                        trade.TakeProfitOrderId = tp.Data.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        await PersistExchangeOrderAsync(trade.UserId, trade, connection, trade.TakeProfitOrderId,
                            protectionSide == OrderSide.Buy ? "BUY" : "SELL", "TakeProfitMarket", true, "TakeProfit", parent.Id);
                    }

                    trade.ProtectionStatus = "Protected";
                    trade.UpdatedAt = DateTime.UtcNow;
                    _context.AuditLogs.Add(new AuditLog
                    {
                        UserId = trade.UserId, Action = "ProtectionOrdersCreated",
                        Details = $"Binance Futures protection created for trade {trade.Id}. SL={trade.StopLossOrderId ?? "none"}, TP={trade.TakeProfitOrderId ?? "none"}.",
                        CreatedAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                    return true;
                }

                if (!entrySide.Equals("BUY", StringComparison.OrdinalIgnoreCase))
                    return false;

                var spotEntry = await binance.SpotApi.Trading.GetOrderAsync(
                    trade.Symbol, numericEntryOrderId, null, null, CancellationToken.None);

                if (!spotEntry.Success || spotEntry.Data == null)
                {
                    trade.ProtectionStatus = "ProtectionPending";
                    trade.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    return false;
                }

                var filledSpotQuantity = Convert.ToDecimal(spotEntry.Data.QuantityFilled, System.Globalization.CultureInfo.InvariantCulture);
                if (filledSpotQuantity <= 0m)
                {
                    trade.ProtectionStatus = "Pending";
                    trade.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    return true;
                }

                if (!trade.StopLoss.HasValue || !trade.TakeProfit.HasValue)
                    return false;

                var oco = await binance.SpotApi.Trading.PlaceOcoOrderAsync(
                    symbol: trade.Symbol, side: OrderSide.Sell, quantity: filledSpotQuantity,
                    price: trade.TakeProfit.Value, stopPrice: trade.StopLoss.Value,
                    stopLimitPrice: trade.StopLoss.Value, stopLimitTimeInForce: TimeInForce.GoodTillCanceled);

                if (!oco.Success || oco.Data == null)
                {
                    trade.ProtectionStatus = "ProtectionPending";
                    trade.Reason = "Binance Spot OCO protection was not accepted; reconciliation will retry.";
                    trade.UpdatedAt = DateTime.UtcNow;
                    _context.AuditLogs.Add(new AuditLog
                    {
                        UserId = trade.UserId, Action = "ProtectionOrdersPartialFailure",
                        Details = $"Binance Spot OCO protection failed for trade {trade.Id}; recovery is required.",
                        CreatedAt = DateTime.UtcNow
                    });
                    await _context.SaveChangesAsync();
                    return false;
                }

                var persistedProtection = await PersistSpotOcoProtectionOrdersAsync(
                    trade,
                    connection,
                    parent.Id,
                    oco.Data,
                    binance);

                if (!persistedProtection)
                {
                    trade.ProtectionStatus = "ProtectionPending";
                    trade.Reason = "Spot OCO was accepted by Binance but its child orders could not be persisted locally; reconciliation will retry.";
                    trade.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();
                    return false;
                }

                trade.ProtectionStatus = "Protected";
                trade.Reason = null;
                trade.UpdatedAt = DateTime.UtcNow;
                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = trade.UserId, Action = "ProtectionOrdersCreated",
                    Details = $"Binance Spot OCO protection created for trade {trade.Id}. SL={trade.StopLossOrderId}, TP={trade.TakeProfitOrderId}.",
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
                return true;
            }
            catch
            {
                trade.ProtectionStatus = "ProtectionPending";
                trade.Reason = "Protection creation failed unexpectedly; reconciliation will retry.";
                trade.UpdatedAt = DateTime.UtcNow;
                try { await _context.SaveChangesAsync(); } catch { }
                return false;
            }
        }


        private async Task<bool> PersistSpotOcoProtectionOrdersAsync(
            Trade trade,
            BrokerConnection connection,
            int parentExchangeOrderId,
            object ocoData,
            BinanceRestClient binance)
        {
            try
            {
                var orders = GetEnumerableProperty(ocoData, "Orders")
                    .Where(x => x != null)
                    .ToList();

                if (orders.Count < 2)
                    return false;

                var reports = GetEnumerableProperty(ocoData, "OrderReports")
                    .Where(x => x != null)
                    .ToList();

                string? stopLossOrderId = null;
                string? takeProfitOrderId = null;

                foreach (var order in orders)
                {
                    var id = GetStringProperty(order!, "OrderId");
                    if (string.IsNullOrWhiteSpace(id))
                        continue;

                    var type = FindOrderTypeForId(reports, id);

                    if (string.IsNullOrWhiteSpace(type) &&
                        long.TryParse(id, out var numericId))
                    {
                        var result = await binance.SpotApi.Trading.GetOrderAsync(
                            trade.Symbol,
                            numericId,
                            null,
                            null,
                            CancellationToken.None);

                        if (result.Success && result.Data != null)
                            type = result.Data.Type.ToString();
                    }

                    if (type.Contains("STOP", StringComparison.OrdinalIgnoreCase))
                        stopLossOrderId = id;
                    else if (type.Contains("LIMIT", StringComparison.OrdinalIgnoreCase))
                        takeProfitOrderId = id;
                }

                if (string.IsNullOrWhiteSpace(stopLossOrderId) ||
                    string.IsNullOrWhiteSpace(takeProfitOrderId))
                    return false;

                trade.StopLossOrderId = stopLossOrderId;
                trade.TakeProfitOrderId = takeProfitOrderId;

                await PersistExchangeOrderIfMissingAsync(
                    trade,
                    connection,
                    stopLossOrderId,
                    "SELL",
                    "StopLoss",
                    "StopLossLimit",
                    parentExchangeOrderId);

                await PersistExchangeOrderIfMissingAsync(
                    trade,
                    connection,
                    takeProfitOrderId,
                    "SELL",
                    "TakeProfit",
                    "LimitMaker",
                    parentExchangeOrderId);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task PersistExchangeOrderIfMissingAsync(
            Trade trade,
            BrokerConnection connection,
            string exchangeOrderId,
            string side,
            string orderRole,
            string orderType,
            int parentExchangeOrderId)
        {
            var existing = await _context.ExchangeOrders.FirstOrDefaultAsync(o =>
                o.UserId == trade.UserId &&
                o.TradeId == trade.Id &&
                o.ExchangeOrderId == exchangeOrderId);

            if (existing != null)
            {
                existing.OrderRole = orderRole;
                existing.OrderType = orderType;
                existing.ParentExchangeOrderId = parentExchangeOrderId;
                existing.UpdatedAt = DateTime.UtcNow;
                return;
            }

            _context.ExchangeOrders.Add(new ExchangeOrder
            {
                UserId = trade.UserId,
                TradeId = trade.Id,
                BrokerName = connection.BrokerName,
                ExchangeOrderId = exchangeOrderId,
                Symbol = trade.Symbol,
                Side = side,
                OrderType = orderType,
                RequestedQuantity = trade.Quantity,
                FilledQuantity = 0m,
                AverageFillPrice = 0m,
                Status = "New",
                IsTestnet = connection.IsTestnet,
                IsFutures = false,
                OrderRole = orderRole,
                ParentExchangeOrderId = parentExchangeOrderId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastReconciledAt = null
            });

            await _context.SaveChangesAsync();
        }

        private static IEnumerable<object?> GetEnumerableProperty(
            object source,
            string propertyName)
        {
            var property = source.GetType().GetProperty(propertyName);
            var value = property?.GetValue(source);

            return value is System.Collections.IEnumerable enumerable
                ? enumerable.Cast<object?>()
                : Enumerable.Empty<object?>();
        }

        private static string GetStringProperty(object source, string propertyName)
        {
            var property = source.GetType().GetProperty(propertyName);
            return property?.GetValue(source)?.ToString() ?? string.Empty;
        }

        private static string FindOrderTypeForId(
            IEnumerable<object?> reports,
            string orderId)
        {
            foreach (var report in reports)
            {
                if (report == null)
                    continue;

                var reportId = GetStringProperty(report, "OrderId");
                if (reportId == orderId)
                    return GetStringProperty(report, "Type");
            }

            return string.Empty;
        }

        private async Task<decimal> GetSpotAssetBalanceAsync(string asset, BrokerConnection connection)
        {
            try
            {
                var client = CreateClient(connection);
                if (client is BinanceRestClient binance)
                {
                    var result = await binance.SpotApi.Account.GetAccountInfoAsync();
                    if (result.Success)
                        return result.Data.Balances.FirstOrDefault(b => b.Asset.Equals(asset, StringComparison.OrdinalIgnoreCase))?.Available ?? 0m;
                }

                if (client is BybitRestClient bybit)
                {
                    var result = await bybit.V5Api.Account.GetBalancesAsync(Bybit.Net.Enums.AccountType.Spot, asset, CancellationToken.None);
                    if (result.Success)
                        return result.Data.List?.FirstOrDefault()?.Assets?.FirstOrDefault(a => a.Asset.Equals(asset, StringComparison.OrdinalIgnoreCase))?.Free ?? 0m;
                }
            }
            catch { }
            return 0m;
        }

        private static string ExtractBaseAsset(string symbol)
        {
            var normalized = symbol.Trim().ToUpperInvariant();
            foreach (var quote in new[] { "USDT", "USDC", "BUSD", "FDUSD", "BTC", "ETH" })
                if (normalized.EndsWith(quote, StringComparison.Ordinal))
                    return normalized[..^quote.Length];
            return normalized;
        }

        private async Task<BrokerOrderResult> AuditOrderFailureAsync(
            int userId, string action, string message, string details)
        {
            _context.AuditLogs.Add(new AuditLog
            { UserId = userId, Action = action, Details = details, CreatedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
            return BrokerOrderResult.Fail(message);
        }

        private async Task AuditOrderEventAsync(
            int userId, string action, string details)
        {
            _context.AuditLogs.Add(new AuditLog
            { UserId = userId, Action = action, Details = details, CreatedAt = DateTime.UtcNow });
            await _context.SaveChangesAsync();
        }

        private async Task PersistExchangeOrderAsync(
            int userId,
            Trade trade,
            BrokerConnection connection,
            string exchangeOrderId,
            string side,
            string orderType,
            bool useFutures,
            string orderRole = "Entry",
            int? parentExchangeOrderId = null)
        {
            var exchangeOrder = new ExchangeOrder
            {
                UserId = userId,
                TradeId = trade.Id,
                BrokerName = connection.BrokerName,
                ExchangeOrderId = exchangeOrderId,
                Symbol = trade.Symbol,
                Side = side,
                OrderType = orderType,
                RequestedQuantity = trade.Quantity,
                FilledQuantity = 0m,
                AverageFillPrice = 0m,
                Status = "New",
                IsTestnet = connection.IsTestnet,
                IsFutures = useFutures,
                OrderRole = orderRole,
                ParentExchangeOrderId = parentExchangeOrderId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastReconciledAt = null
            };

            _context.ExchangeOrders.Add(exchangeOrder);

            await _context.SaveChangesAsync();
        }

        public async Task<bool> CancelOrderAsync(
            int userId, string symbol, string orderId, bool useFutures = false)
        {
            symbol = symbol?.Trim().ToUpperInvariant() ?? string.Empty;
            orderId = orderId?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(symbol) ||
                string.IsNullOrWhiteSpace(orderId))
            {
                await AuditOrderEventAsync(
                    userId,
                    "OrderCancellationFailed",
                    "Order cancellation was rejected because the symbol or order ID was missing.");
                return false;
            }

            // Verify local ownership before making an exchange cancellation
            // request. This keeps arbitrary order IDs out of the exchange call.
            var localOrder = await _context.ExchangeOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(o =>
                    o.UserId == userId &&
                    o.ExchangeOrderId == orderId &&
                    o.Symbol == symbol);

            if (localOrder == null)
            {
                await AuditOrderEventAsync(
                    userId,
                    "OrderCancellationFailed",
                    $"Order {orderId} cancellation was rejected because no matching local order exists for {symbol}.");
                return false;
            }

            if (string.Equals(localOrder.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(localOrder.Status, "FILLED", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(localOrder.Status, "REJECTED", StringComparison.OrdinalIgnoreCase))
            {
                await AuditOrderEventAsync(
                    userId,
                    "OrderCancellationFailed",
                    $"Order {orderId} cancellation was rejected because the local order is already in terminal state '{localOrder.Status}'.");
                return false;
            }

            var connection = await GetConnectionAsync(userId);

            if (connection == null || !connection.IsConnected)
            {
                await AuditOrderEventAsync(userId, "OrderCancellationFailed",
                    $"Order {orderId} cancellation failed because no active broker connection exists.");
                return false;
            }

            try
            {
                var client = CreateClient(connection);

                if (client is BinanceRestClient binance && long.TryParse(orderId, out var numericOrderId))
                {
                    bool success;
                    if (useFutures)
                    {
                        success = (await binance.UsdFuturesApi.Trading.CancelOrderAsync(symbol, numericOrderId)).Success;
                    }
                    else
                    {
                        success = (await binance.SpotApi.Trading.CancelOrderAsync(symbol, numericOrderId)).Success;
                    }

                    await AuditOrderEventAsync(userId, success ? "OrderCancelled" : "OrderCancellationFailed",
                        $"Order {orderId} cancellation on {connection.BrokerName} for {symbol} {(success ? "succeeded" : "failed")}.");
                    return success;
                }

                if (client is BybitRestClient bybit)
                {
                    var category = useFutures ? Bybit.Net.Enums.Category.Linear : Bybit.Net.Enums.Category.Spot;
                    var success = (await bybit.V5Api.Trading.CancelOrderAsync(category, symbol, orderId)).Success;
                    await AuditOrderEventAsync(userId, success ? "OrderCancelled" : "OrderCancellationFailed",
                        $"Order {orderId} cancellation on {connection.BrokerName} for {symbol} {(success ? "succeeded" : "failed")}.");
                    return success;
                }

                await AuditOrderEventAsync(userId, "OrderCancellationFailed",
                    $"Order {orderId} cancellation failed because the broker '{connection.BrokerName}' or order ID format is unsupported.");
            }
            catch
            {
                await AuditOrderEventAsync(userId, "OrderCancellationFailed",
                    $"Order {orderId} cancellation failed for {symbol} on {connection.BrokerName}.");
            }

            return false;
        }


        public async Task<bool> CancelOpenExchangeOrderForUserAsync(
            int userId,
            string symbol,
            string orderId,
            bool useFutures = false)
        {
            symbol = symbol?.Trim().ToUpperInvariant() ?? string.Empty;
            orderId = orderId?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(symbol) ||
                string.IsNullOrWhiteSpace(orderId))
                return false;

            var connection = await GetConnectionAsync(userId);

            if (connection == null ||
                !connection.IsConnected ||
                !connection.IsTestnet ||
                !connection.BrokerName.Equals("Binance", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!long.TryParse(orderId, out var numericOrderId))
                return false;

            try
            {
                var client = CreateClient(connection);
                if (client is not BinanceRestClient binance)
                    return false;

                // Only allow cancellation of an order that Binance currently
                // reports as open for this exact user's own testnet account.
                var openOrders = await binance.SpotApi.Trading.GetOpenOrdersAsync(symbol);
                if (!openOrders.Success || openOrders.Data == null)
                    return false;

                var isOpen = openOrders.Data.Any(o =>
                    o.Id == numericOrderId);

                if (!isOpen)
                    return false;

                var result = await binance.SpotApi.Trading.CancelOrderAsync(
                    symbol,
                    numericOrderId);

                await AuditOrderEventAsync(
                    userId,
                    result.Success ? "TestnetOpenOrderCancelled" : "TestnetOpenOrderCancellationFailed",
                    $"Testnet Binance open order {orderId} cancellation for {symbol}: {(result.Success ? "succeeded" : "failed")}.");

                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        public async Task<object?> GetOpenOrdersAsync(
            int userId,
            string? symbol = null,
            bool useFutures = false)
        {
            var connection =
                await GetConnectionAsync(userId);

            if (connection == null ||
                !connection.IsConnected)
            {
                return null;
            }

            try
            {
                var client =
                    CreateClient(connection);

                if (client is BinanceRestClient binance)
                {
                    if (useFutures)
                    {
                        var result =
                            await binance
                                .UsdFuturesApi
                                .Trading
                                .GetOpenOrdersAsync(
                                    symbol);

                        return result.Success
                            ? result.Data
                            : null;
                    }

                    var spot =
                        await binance
                            .SpotApi
                            .Trading
                            .GetOpenOrdersAsync(
                                symbol);

                    return spot.Success
                        ? spot.Data
                        : null;
                }

                if (client is BybitRestClient bybit)
                {
                    var category =
                        useFutures
                            ? Bybit.Net.Enums.Category.Linear
                            : Bybit.Net.Enums.Category.Spot;

                    var result =
                        await bybit
                            .V5Api
                            .Trading
                            .GetOrdersAsync(
                                category,
                                symbol);

                    return result.Success
                        ? result.Data
                        : null;
                }
            }
            catch
            {
            }

            return null;
        }

        private async Task<BrokerConnection?>
            GetConnectionAsync(int userId, int? connectionId = null)
        {
            var query = _context.BrokerConnections
                .Where(b =>
                    b.UserId == userId &&
                    b.IsActive &&
                    b.IsConnected);

            if (connectionId.HasValue)
            {
                return await query.FirstOrDefaultAsync(b => b.Id == connectionId.Value);
            }

            return await query
                .OrderByDescending(b => b.LastConnectedAt)
                .ThenByDescending(b => b.Id)
                .FirstOrDefaultAsync();
        }

        internal object CreateClient(
            BrokerConnection connection)
        {
            var apiKey =
                _protector.Unprotect(
                    connection.ApiKey);

            var apiSecret =
                string.IsNullOrWhiteSpace(
                    connection.ApiSecret)
                    ? null
                    : _protector.Unprotect(
                        connection.ApiSecret);

            if (string.IsNullOrWhiteSpace(apiSecret))
            {
                throw new InvalidOperationException(
                    "Broker secret is missing.");
            }

            if (connection.BrokerName.Equals(
                    "binance",
                    StringComparison.OrdinalIgnoreCase))
            {
                var client =
                    new BinanceRestClient(options =>
                    {
                        options.Environment =
                            connection.IsTestnet
                                ? BinanceEnvironment.Testnet
                                : BinanceEnvironment.Live;
                    });

                client.SetApiCredentials(
                    new ApiCredentials(
                        apiKey,
                        apiSecret));

                return client;
            }

            if (connection.BrokerName.Equals(
                    "bybit",
                    StringComparison.OrdinalIgnoreCase))
            {
                var client =
                    new BybitRestClient(options =>
                    {
                        options.Environment =
                            connection.IsTestnet
                                ? BybitEnvironment.Testnet
                                : BybitEnvironment.Live;
                    });

                client.SetApiCredentials(
                    new ApiCredentials(
                        apiKey,
                        apiSecret));

                return client;
            }

            throw new InvalidOperationException(
                "Unsupported broker.");
        }

        private static async Task<bool>
            VerifyConnectionWithRetryAsync(
                object client,
                BrokerConnection connection)
        {
            const int maxAttempts = 3;
            var delay = TimeSpan.FromSeconds(1);

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    if (await VerifyConnectionAsync(client, connection))
                        return true;
                }
                catch when (attempt < maxAttempts)
                {
                    // Verification is a read-only operation, so retrying a
                    // transient DNS/network/API failure is safe.
                }

                if (attempt < maxAttempts)
                {
                    await Task.Delay(delay);
                    delay = TimeSpan.FromSeconds(delay.TotalSeconds * 2);
                }
            }

            return false;
        }

        private static async Task<bool>
            VerifyConnectionAsync(
                object client,
                BrokerConnection connection)
        {
            if (client is BinanceRestClient binance)
            {
                var result =
                    await binance
                        .SpotApi
                        .Account
                        .GetAccountInfoAsync();

                return result.Success;
            }

            if (client is BybitRestClient bybit)
            {
                var result =
                    await bybit
                        .V5Api
                        .Account
                        .GetMarginAccountInfoAsync(
                            CancellationToken.None);

                return result.Success;
            }

            return false;
        }
    }

    public sealed class BrokerConnectionStatus
    {
        public int Id { get; set; }

        public string BrokerName { get; set; }
            = string.Empty;

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

        public static BrokerOrderResult SuccessResult(
            string? orderId,
            decimal? price) =>
            new()
            {
                Success = true,
                OrderId = orderId,
                Price = price
            };

        public static BrokerOrderResult Fail(
            string error) =>
            new()
            {
                Success = false,
                Error = error
            };
    }
}



