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

        public BrokerService(
            ApplicationDbContext context,
            ICredentialProtector protector)
        {
            _context = context;
            _protector = protector;
        }

        public async Task<bool> ConnectAsync(
            int userId,
            BrokerConnection connection)
        {
            var persisted = await _context.BrokerConnections
                .FirstOrDefaultAsync(b =>
                    b.UserId == userId &&
                    b.BrokerName == connection.BrokerName);

            if (persisted == null)
            {
                persisted = connection;
                persisted.UserId = userId;

                persisted.IsTestnet = true;
                persisted.IsLiveTradingEnabled = false;
                persisted.LiveTradingOptInAt = null;

                persisted.ApiKey =
                    _protector.Protect(connection.ApiKey);

                persisted.ApiSecret =
                    string.IsNullOrWhiteSpace(connection.ApiSecret)
                        ? null
                        : _protector.Protect(connection.ApiSecret);

                _context.BrokerConnections.Add(persisted);
            }

            try
            {
                var client = CreateClient(persisted);

                var connected =
                    await VerifyConnectionAsync(client, persisted);

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
            string? brokerName = null)
        {
            var connections = await _context.BrokerConnections
                .Where(b =>
                    b.UserId == userId &&
                    b.IsActive &&
                    (brokerName == null ||
                     b.BrokerName == brokerName))
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

        public virtual async Task<decimal> GetBalanceAsync(
            int userId,
            string currency = "USDT",
            bool useFutures = false)
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
            string? idempotencyKey = null)
        {
            symbol = symbol?.Trim() ?? string.Empty;
            direction = direction?.Trim() ?? string.Empty;
            orderType = orderType?.Trim() ?? string.Empty;

            // 4.8: caller does not provide quantity.
            if (string.IsNullOrWhiteSpace(symbol))
            {
                return BrokerOrderResult.Fail(
                    "Invalid order parameters.");
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
                return BrokerOrderResult.Fail(
                    "Invalid order side.");
            }

            if (string.IsNullOrWhiteSpace(idempotencyKey))
            {
                return BrokerOrderResult.Fail(
                    "An idempotency key is required for every order.");
            }

            idempotencyKey = idempotencyKey.Trim();

            if (idempotencyKey.Length > 100)
            {
                return BrokerOrderResult.Fail(
                    "Invalid idempotency key.");
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
                return BrokerOrderResult.Fail(
                    "Invalid order type.");
            }

            if (string.Equals(
                    orderType,
                    "Limit",
                    StringComparison.OrdinalIgnoreCase) &&
                (!entryPrice.HasValue ||
                 entryPrice.Value <= 0))
            {
                return BrokerOrderResult.Fail(
                    "A valid entry price is required for limit orders.");
            }

            var connection =
                await GetConnectionAsync(userId);

            if (connection == null ||
                !connection.IsConnected)
            {
                return BrokerOrderResult.Fail(
                    "No active broker connection.");
            }

            if (!connection.IsTestnet &&
                !connection.IsLiveTradingEnabled)
            {
                return BrokerOrderResult.Fail(
                    "Live trading is not explicitly enabled for this connection.");
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
                    return BrokerOrderResult.SuccessResult(
                        existingTrade.OrderId,
                        existingTrade.EntryPrice);
                }

                return BrokerOrderResult.Fail(
                    "An order with this idempotency key is already " +
                    "being processed. Reconciliation is required " +
                    "before retrying.");
            }

            // ---------------------------------------------------------
            // SERVER-SIDE RISK SIZING
            // ---------------------------------------------------------

            var riskSetting = await _context.RiskSettings
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.UserId == userId);

            if (riskSetting == null)
            {
                return BrokerOrderResult.Fail(
                    "Risk settings are not configured for this user.");
            }

            if (riskSetting.RiskPerTrade <= 0m ||
                riskSetting.RiskPerTrade > 100m)
            {
                return BrokerOrderResult.Fail(
                    "Configured risk per trade is invalid.");
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
                useFutures);

            if (balance <= 0m)
            {
                return BrokerOrderResult.Fail(
                    "Insufficient available balance.");
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
                return BrokerOrderResult.Fail(
                    "Current account equity is not available.");
            }

            // Reload the tracked RiskSetting so the high-water mark can
            // be safely persisted.
            var trackedRiskSetting = await _context.RiskSettings
                .FirstOrDefaultAsync(r => r.UserId == userId);

            if (trackedRiskSetting == null)
            {
                return BrokerOrderResult.Fail(
                    "Risk settings are not configured for this user.");
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

            var marketDataService =
                new ExchangeMarketDataService();

            ExchangeOrderRules rules;

            if (string.Equals(
                    connection.BrokerName,
                    "Binance",
                    StringComparison.OrdinalIgnoreCase))
            {
                rules = await marketDataService.GetBinanceRulesAsync(
                    symbol,
                    useFutures);
            }
            else if (string.Equals(
                         connection.BrokerName,
                         "Bybit",
                         StringComparison.OrdinalIgnoreCase))
            {
                rules = await marketDataService.GetBybitRulesAsync(
                    symbol,
                    useFutures);
            }
            else
            {
                return BrokerOrderResult.Fail(
                    "Unsupported broker.");
            }

            var sizingPrice =
                entryPrice ?? rules.ReferencePrice;

            if (sizingPrice <= 0m)
            {
                return BrokerOrderResult.Fail(
                    "Unable to determine a valid pricing reference.");
            }

            var leverage = useFutures
                ? riskSetting.Leverage
                : 1m;

            if (leverage <= 0m)
            {
                return BrokerOrderResult.Fail(
                    "Configured leverage is invalid.");
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
                return BrokerOrderResult.Fail(
                    "Calculated order quantity is invalid.");
            }

            if (rules.QuantityStep <= 0m)
            {
                return BrokerOrderResult.Fail(
                    "Exchange quantity step is invalid.");
            }

            // Normalize DOWN only.
            // Never round up because doing so could increase risk.
            calculatedQuantity =
                Math.Floor(
                    calculatedQuantity / rules.QuantityStep)
                * rules.QuantityStep;

            if (calculatedQuantity <= 0m)
            {
                return BrokerOrderResult.Fail(
                    "Calculated order quantity became zero after exchange rounding.");
            }

            if (calculatedQuantity < rules.MinQuantity)
            {
                return BrokerOrderResult.Fail(
                    "Calculated quantity is below the exchange minimum.");
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
                return BrokerOrderResult.Fail(
                    "Exchange quantity limits cannot satisfy the calculated order size.");
            }

            var finalNotional =
                calculatedQuantity * sizingPrice;

            if (finalNotional <= 0m)
            {
                return BrokerOrderResult.Fail(
                    "Final order value is invalid.");
            }

            if (rules.MinNotional > 0m &&
                finalNotional < rules.MinNotional)
            {
                return BrokerOrderResult.Fail(
                    "Final order value is below the exchange minimum.");
            }

            // Spot orders must never exceed the available quote balance.
            if (!useFutures &&
                finalNotional > balance)
            {
                return BrokerOrderResult.Fail(
                    "Final order value exceeds available balance.");
            }

            // Server-authoritative quantity.
            var quantity = calculatedQuantity;

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
                        return BrokerOrderResult.SuccessResult(
                            concurrentTrade.OrderId,
                            concurrentTrade.EntryPrice);
                    }

                    return BrokerOrderResult.Fail(
                        "An order with this idempotency key is already " +
                        "being processed. Reconciliation is required " +
                        "before retrying.");
                }

                return BrokerOrderResult.Fail(
                    "The order could not be reserved safely. " +
                    "No exchange order was submitted.");
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
                            pendingTrade.Status = "Failed";
                            pendingTrade.UpdatedAt =
                                DateTime.UtcNow;
                            pendingTrade.Reason =
                                "Exchange rejected the order.";

                            await _context.SaveChangesAsync();

                            return BrokerOrderResult.Fail(
                                "Exchange rejected the order.");
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

                        pendingTrade.Status = "Open";
                        pendingTrade.UpdatedAt =
                            DateTime.UtcNow;

                        await PersistExchangeOrderAsync(
                            userId,
                            pendingTrade,
                            connection,
                            orderId,
                            normalizedSide,
                            orderType,
                            useFutures);

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

                    var spot =
                        await binance
                            .SpotApi
                            .Trading
                            .PlaceOrderAsync(
                                symbol: symbol,
                                side: side,

                                type:
                                    string.Equals(
                                        orderType,
                                        "Market",
                                        StringComparison.OrdinalIgnoreCase)
                                        ? SpotOrderType.Market
                                        : SpotOrderType.Limit,

                                quantity: quantity,
                                price: entryPrice);

                    if (!spot.Success)
                    {
                        pendingTrade.Status = "Failed";
                        pendingTrade.UpdatedAt =
                            DateTime.UtcNow;
                        pendingTrade.Reason =
                            "Exchange rejected the order.";

                        await _context.SaveChangesAsync();

                        return BrokerOrderResult.Fail(
                            "Exchange rejected the order.");
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

                    pendingTrade.Status = "Open";
                    pendingTrade.UpdatedAt =
                        DateTime.UtcNow;

                    await PersistExchangeOrderAsync(
                        userId,
                        pendingTrade,
                        connection,
                        spotOrderId,
                        normalizedSide,
                        orderType,
                        useFutures);

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
                        pendingTrade.Status = "Failed";
                        pendingTrade.UpdatedAt =
                            DateTime.UtcNow;
                        pendingTrade.Reason =
                            "Exchange rejected the order.";

                        await _context.SaveChangesAsync();

                        return BrokerOrderResult.Fail(
                            "Exchange rejected the order.");
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

                        await _context.SaveChangesAsync();

                        return BrokerOrderResult.Fail(
                            "The exchange accepted the order but " +
                            "did not return an order ID. " +
                            "Reconciliation is required.");
                    }

                    pendingTrade.OrderId =
                        bybitOrderId;

                    pendingTrade.Status = "Open";
                    pendingTrade.UpdatedAt =
                        DateTime.UtcNow;

                    await PersistExchangeOrderAsync(
                        userId,
                        pendingTrade,
                        connection,
                        bybitOrderId,
                        normalizedSide,
                        orderType,
                        useFutures);

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

                await _context.SaveChangesAsync();

                return BrokerOrderResult.Fail(
                    "Unsupported broker.");
            }
            catch
            {
                pendingTrade.Status =
                    "PendingReconciliation";

                pendingTrade.UpdatedAt =
                    DateTime.UtcNow;

                pendingTrade.Reason =
                    "Exchange submission result is unknown; " +
                    "reconciliation is required.";

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


        private async Task PersistExchangeOrderAsync(
            int userId,
            Trade trade,
            BrokerConnection connection,
            string exchangeOrderId,
            string side,
            string orderType,
            bool useFutures)
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
                Status = "NEW",
                IsTestnet = connection.IsTestnet,
                IsFutures = useFutures,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastReconciledAt = null
            };

            _context.ExchangeOrders.Add(exchangeOrder);

            await _context.SaveChangesAsync();
        }

        public async Task<bool> CancelOrderAsync(
            int userId,
            string symbol,
            string orderId,
            bool useFutures = false)
        {
            var connection =
                await GetConnectionAsync(userId);

            if (connection == null ||
                !connection.IsConnected)
            {
                return false;
            }

            try
            {
                var client =
                    CreateClient(connection);

                if (client is BinanceRestClient binance &&
                    long.TryParse(
                        orderId,
                        out var numericOrderId))
                {
                    if (useFutures)
                    {
                        return (
                            await binance
                                .UsdFuturesApi
                                .Trading
                                .CancelOrderAsync(
                                    symbol,
                                    numericOrderId))
                            .Success;
                    }

                    return (
                        await binance
                            .SpotApi
                            .Trading
                            .CancelOrderAsync(
                                symbol,
                                numericOrderId))
                        .Success;
                }

                if (client is BybitRestClient bybit)
                {
                    var category =
                        useFutures
                            ? Bybit.Net.Enums.Category.Linear
                            : Bybit.Net.Enums.Category.Spot;

                    return (
                        await bybit
                            .V5Api
                            .Trading
                            .CancelOrderAsync(
                                category,
                                symbol,
                                orderId))
                        .Success;
                }
            }
            catch
            {
            }

            return false;
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
            GetConnectionAsync(int userId)
        {
            return await _context.BrokerConnections
                .FirstOrDefaultAsync(b =>
                    b.UserId == userId &&
                    b.IsActive &&
                    b.IsConnected);
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