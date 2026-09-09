using Binance.Net.Clients;
using Bybit.Net.Clients;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Reflection;
using TradingBotEngine.Data;
using TradingBotEngine.Data.Models;

namespace TradingBotEngine.Services
{
    public class ReconciliationService : IReconciliationService
    {
        private readonly ApplicationDbContext _context;
        private readonly BrokerService _brokerService;

        public ReconciliationService(
            ApplicationDbContext context,
            BrokerService brokerService)
        {
            _context = context;
            _brokerService = brokerService;
        }

        public async Task<bool> ReconcileOrderAsync(
            int userId,
            int exchangeOrderId)
        {
            var order = await _context.ExchangeOrders
                .Include(o => o.Trade)
                .FirstOrDefaultAsync(o =>
                    o.Id == exchangeOrderId &&
                    o.UserId == userId);

            if (order == null || string.IsNullOrWhiteSpace(order.ExchangeOrderId))
                return false;

            var connection = await _context.BrokerConnections
                .FirstOrDefaultAsync(c =>
                    c.UserId == userId &&
                    c.IsActive &&
                    c.BrokerName == order.BrokerName &&
                    c.IsTestnet == order.IsTestnet);

            if (connection == null)
            {
                order.Status = "ReconciliationFailed";
                order.FailureReason = "No active broker connection matches the persisted order environment.";
                order.LastReconciledAt = DateTime.UtcNow;
                order.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return false;
            }

            try
            {
                var client = _brokerService.CreateClient(connection);

                if (client is BinanceRestClient binance)
                {
                    if (!long.TryParse(order.ExchangeOrderId, out var numericOrderId))
                    {
                        order.Status = "ReconciliationFailed";
                        order.FailureReason = "Binance order ID is not numeric.";
                        order.LastReconciledAt = DateTime.UtcNow;
                        order.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();
                        return false;
                    }

                    if (order.IsFutures)
                    {
                        var result = await binance.UsdFuturesApi.Trading.GetOrderAsync(
                            order.Symbol,
                            numericOrderId,
                            null,
                            null,
                            CancellationToken.None);

                        if (!result.Success || result.Data == null)
                            return await MarkReconciliationFailureAsync(
                                order,
                                "Binance Futures order lookup failed.");

                        order.Status = NormalizeBinanceStatus(result.Data.Status.ToString());
                        order.RequestedQuantity = ToDecimal(result.Data.Quantity);
                        order.FilledQuantity = ToDecimal(result.Data.QuantityFilled);
                        order.AverageFillPrice = ToDecimal(result.Data.AveragePrice);
                        order.UpdatedAt = DateTime.UtcNow;
                        order.LastReconciledAt = DateTime.UtcNow;

                        await ReconcileBinanceFuturesFillsAsync(
                            binance,
                            order,
                            numericOrderId);
                    }
                    else
                    {
                        var result = await binance.SpotApi.Trading.GetOrderAsync(
                            order.Symbol,
                            numericOrderId,
                            null,
                            null,
                            CancellationToken.None);

                        if (!result.Success || result.Data == null)
                            return await MarkReconciliationFailureAsync(
                                order,
                                "Binance Spot order lookup failed.");

                        order.Status = NormalizeBinanceStatus(result.Data.Status.ToString());
                        order.RequestedQuantity = ToDecimal(result.Data.Quantity);
                        order.FilledQuantity = ToDecimal(result.Data.QuantityFilled);
                        order.AverageFillPrice = ToDecimal(result.Data.AverageFillPrice);
                        order.UpdatedAt = DateTime.UtcNow;
                        order.LastReconciledAt = DateTime.UtcNow;

                        await ReconcileBinanceSpotFillsAsync(
                            binance,
                            order,
                            numericOrderId);
                    }
                }
                else if (client is BybitRestClient bybit)
                {
                    var category = order.IsFutures
                        ? Bybit.Net.Enums.Category.Linear
                        : Bybit.Net.Enums.Category.Spot;

                    var result = await bybit.V5Api.Trading.GetOrderHistoryAsync(
                        category,
                        order.Symbol,
                        null,
                        null,
                        order.ExchangeOrderId,
                        null,
                        null,
                        null,
                        null,
                        1,
                        null,
                        CancellationToken.None);

                    var exchangeOrder = result.Success
                        ? result.Data?.List?.FirstOrDefault()
                        : null;

                    if (exchangeOrder == null)
                        return await MarkReconciliationFailureAsync(
                            order,
                            "Bybit order lookup failed.");

                    order.Status = NormalizeBybitStatus(exchangeOrder.Status.ToString());
                    order.RequestedQuantity = ToDecimal(exchangeOrder.Quantity);
                    order.FilledQuantity = ToDecimal(exchangeOrder.QuantityFilled);
                    order.AverageFillPrice = ToDecimal(exchangeOrder.AveragePrice);
                    order.UpdatedAt = DateTime.UtcNow;
                    order.LastReconciledAt = DateTime.UtcNow;

                    var rejectReason = exchangeOrder.RejectReason?.ToString();

                    if (!string.IsNullOrWhiteSpace(rejectReason))
                        order.FailureReason = rejectReason;
                    else if (IsTerminalFailure(order.Status))
                        order.FailureReason = $"Exchange status: {order.Status}";
                    else
                        order.FailureReason = null;

                    await ReconcileBybitFillsAsync(
                        bybit,
                        order,
                        category);
                }
                else
                {
                    return await MarkReconciliationFailureAsync(
                        order,
                        "Unsupported broker client.");
                }

                UpdateTradeFromOrder(order);

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "OrderReconciled",
                    Details =
                        $"Exchange order {order.ExchangeOrderId} reconciled for " +
                        $"{order.BrokerName} in {(order.IsTestnet ? "testnet" : "live")} mode. " +
                        $"Status: {order.Status}.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
                return true;
            }
            catch
            {
                return await MarkReconciliationFailureAsync(
                    order,
                    "Exchange reconciliation failed unexpectedly.");
            }
        }

        public async Task<bool> ReconcileBalanceAsync(
            int userId,
            string currency = "USDT",
            bool useFutures = false)
        {
            var connection = await _context.BrokerConnections
                .FirstOrDefaultAsync(c =>
                    c.UserId == userId &&
                    c.IsActive);

            if (connection == null)
                return false;

            try
            {
                var balance = await _brokerService.GetBalanceAsync(
                    userId,
                    currency,
                    useFutures);

                if (balance < 0m)
                    return false;

                var existingBalance = await _context.ExchangeBalances
                    .FirstOrDefaultAsync(b =>
                        b.UserId == userId &&
                        b.BrokerName == connection.BrokerName &&
                        b.Currency == currency &&
                        b.IsTestnet == connection.IsTestnet &&
                        b.IsFutures == useFutures);

                if (existingBalance == null)
                {
                    existingBalance = new ExchangeBalance
                    {
                        UserId = userId,
                        BrokerName = connection.BrokerName,
                        Currency = currency,
                        AvailableBalance = balance,
                        TotalBalance = balance,
                        IsTestnet = connection.IsTestnet,
                        IsFutures = useFutures,
                        CapturedAt = DateTime.UtcNow
                    };

                    _context.ExchangeBalances.Add(existingBalance);
                }
                else
                {
                    existingBalance.AvailableBalance = balance;
                    existingBalance.TotalBalance = balance;
                    existingBalance.CapturedAt = DateTime.UtcNow;
                }

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "BalanceReconciled",
                    Details =
                        $"Balance reconciled and persisted for {connection.BrokerName} " +
                        $"{currency} in {(connection.IsTestnet ? "testnet" : "live")} mode. " +
                        $"Account type: {(useFutures ? "futures" : "spot")}. " +
                        $"Available balance: {balance}.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
                return true;
            }
            catch
            {
                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "BalanceReconciliationFailed",
                    Details =
                        $"Balance reconciliation failed for {connection.BrokerName} " +
                        $"{currency} in {(connection.IsTestnet ? "testnet" : "live")} mode. " +
                        $"Account type: {(useFutures ? "futures" : "spot")}.",
                    CreatedAt = DateTime.UtcNow
                });

                try
                {
                    await _context.SaveChangesAsync();
                }
                catch
                {
                    // Do not expose exchange/database internals.
                }

                return false;
            }
        }

        public async Task<int> ReconcilePositionsAsync(
            int userId,
            bool useFutures = true)
        {
            // Position.cs represents trading positions, so this reconciliation
            // is intentionally limited to futures accounts.
            if (!useFutures)
                return 0;

            var connection = await _context.BrokerConnections
                .FirstOrDefaultAsync(c =>
                    c.UserId == userId &&
                    c.IsActive);

            if (connection == null)
                return 0;

            try
            {
                var client = _brokerService.CreateClient(connection);
                var reconciled = 0;
                var exchangeKeys = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);

                if (client is BinanceRestClient binance)
                {
                    // Binance.Net 9.5.0:
                    // GetPositionInformationAsync(symbol, receiveWindow, ct)
                    // Passing null symbol retrieves the account's futures positions.
                    var result = await binance.UsdFuturesApi.Account
                        .GetPositionInformationAsync(
                            null,
                            null,
                            CancellationToken.None);

                    if (!result.Success || result.Data == null)
                    {
                        await WritePositionFailureAuditAsync(
                            userId,
                            connection,
                            "Binance Futures position lookup failed.");

                        return 0;
                    }

                    foreach (var rawPosition in result.Data)
                    {
                        var symbol = GetStringProperty(
                            rawPosition,
                            "Symbol");

                        if (string.IsNullOrWhiteSpace(symbol))
                            continue;

                        // Binance position models have used PositionAmount/
                        // PositionAmt depending on the endpoint/model.
                        // Reflection here keeps this reconciliation layer
                        // tolerant of the installed SDK model naming.
                        var rawQuantity = GetDecimalProperty(
                            rawPosition,
                            "PositionAmount",
                            "PositionAmt",
                            "Quantity");

                        if (rawQuantity == 0m)
                            continue;

                        var direction = ResolveBinancePositionDirection(
                            rawPosition,
                            rawQuantity);

                        var quantity = Math.Abs(rawQuantity);

                        var entryPrice = GetDecimalProperty(
                            rawPosition,
                            "EntryPrice",
                            "AveragePrice");

                        var currentPrice = GetDecimalProperty(
                            rawPosition,
                            "MarkPrice",
                            "CurrentPrice");

                        var unrealizedPnl = GetDecimalProperty(
                            rawPosition,
                            "UnrealizedProfit",
                            "UnrealizedPnl");

                        await UpsertPositionAsync(
                            userId,
                            symbol,
                            direction,
                            entryPrice,
                            currentPrice,
                            quantity,
                            unrealizedPnl);

                        exchangeKeys.Add(
                            BuildPositionKey(symbol, direction));

                        reconciled++;
                    }
                }
                else if (client is BybitRestClient bybit)
                {
                    // Bybit.Net 3.5.0:
                    // GetPositionsAsync(category, symbol, settleCoin,
                    // baseCoin, limit, cursor, ct)
                    var result = await bybit.V5Api.Trading.GetPositionsAsync(
                        Bybit.Net.Enums.Category.Linear,
                        null,
                        null,
                        null,
                        null,
                        null,
                        CancellationToken.None);

                    if (!result.Success || result.Data?.List == null)
                    {
                        await WritePositionFailureAuditAsync(
                            userId,
                            connection,
                            "Bybit Linear position lookup failed.");

                        return 0;
                    }

                    foreach (var rawPosition in result.Data.List)
                    {
                        var symbol = rawPosition.Symbol;

                        if (string.IsNullOrWhiteSpace(symbol))
                            continue;

                        var quantity = ToDecimal(rawPosition.Quantity);

                        if (quantity <= 0m)
                            continue;

                        var direction =
                            rawPosition.Side.ToString().Equals(
                                "Sell",
                                StringComparison.OrdinalIgnoreCase)
                            ? "Short"
                            : "Long";

                        var entryPrice =
                            ToDecimal(rawPosition.AveragePrice);

                        var currentPrice =
                            ToDecimal(rawPosition.MarkPrice);

                        var unrealizedPnl =
                            ToDecimal(rawPosition.UnrealizedPnl);

                        await UpsertPositionAsync(
                            userId,
                            symbol,
                            direction,
                            entryPrice,
                            currentPrice,
                            quantity,
                            unrealizedPnl);

                        exchangeKeys.Add(
                            BuildPositionKey(symbol, direction));

                        reconciled++;
                    }
                }
                else
                {
                    await WritePositionFailureAuditAsync(
                        userId,
                        connection,
                        "Unsupported broker client for futures position reconciliation.");

                    return 0;
                }

                // Only close local positions after a successful exchange
                // snapshot. A failed exchange lookup must never make every
                // local position magically disappear.
                var localOpenPositions = await _context.Positions
                    .Where(p =>
                        p.UserId == userId &&
                        p.Status == "Open")
                    .ToListAsync();

                foreach (var localPosition in localOpenPositions)
                {
                    var key = BuildPositionKey(
                        localPosition.Symbol,
                        localPosition.Direction);

                    if (exchangeKeys.Contains(key))
                        continue;

                    localPosition.Status = "Closed";
                    localPosition.ClosedAt ??= DateTime.UtcNow;
                }

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "PositionsReconciled",
                    Details =
                        $"Futures positions reconciled for {connection.BrokerName} " +
                        $"in {(connection.IsTestnet ? "testnet" : "live")} mode. " +
                        $"Exchange open positions processed: {reconciled}.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync();
                return reconciled;
            }
            catch
            {
                await WritePositionFailureAuditAsync(
                    userId,
                    connection,
                    "Futures position reconciliation failed unexpectedly.");

                return 0;
            }
        }

        public async Task<int> ReconcilePendingOrdersAsync(int userId)
        {
            var orders = await _context.ExchangeOrders
                .Where(o =>
                    o.UserId == userId &&
                    (o.Status == "Pending" ||
                     o.Status == "New" ||
                     o.Status == "PartiallyFilled" ||
                     o.Status == "PendingReconciliation"))
                .Select(o => o.Id)
                .ToListAsync();

            var reconciled = 0;

            foreach (var orderId in orders)
            {
                if (await ReconcileOrderAsync(userId, orderId))
                    reconciled++;
            }

            return reconciled;
        }

        private async Task UpsertPositionAsync(
            int userId,
            string symbol,
            string direction,
            decimal entryPrice,
            decimal currentPrice,
            decimal quantity,
            decimal unrealizedPnl)
        {
            var normalizedSymbol =
                symbol.Trim().ToUpperInvariant();

            var normalizedDirection =
                direction.Trim();

            var position = await _context.Positions
                .FirstOrDefaultAsync(p =>
                    p.UserId == userId &&
                    p.Symbol == normalizedSymbol &&
                    p.Direction == normalizedDirection &&
                    p.Status == "Open");

            if (position == null)
            {
                position = new Position
                {
                    UserId = userId,
                    Symbol = normalizedSymbol,
                    Direction = normalizedDirection,
                    EntryPrice = entryPrice,
                    CurrentPrice = currentPrice,
                    Quantity = quantity,

                    // The exchange position endpoint is the source of truth
                    // for the position itself. We do not invent SL/TP values.
                    StopLoss = 0m,
                    TakeProfit = 0m,

                    UnrealizedPnL = unrealizedPnl,
                    UnrealizedPnLPercentage =
                        CalculatePnLPercentage(
                            entryPrice,
                            quantity,
                            unrealizedPnl),

                    Status = "Open",
                    OpenedAt = DateTime.UtcNow
                };

                _context.Positions.Add(position);
                return;
            }

            // Preserve the existing local SL/TP configuration.
            position.EntryPrice =
                entryPrice > 0m
                    ? entryPrice
                    : position.EntryPrice;

            position.CurrentPrice =
                currentPrice > 0m
                    ? currentPrice
                    : position.CurrentPrice;

            position.Quantity = quantity;
            position.UnrealizedPnL = unrealizedPnl;

            position.UnrealizedPnLPercentage =
                CalculatePnLPercentage(
                    position.EntryPrice,
                    quantity,
                    unrealizedPnl);

            position.Status = "Open";
            position.ClosedAt = null;
        }

        private static decimal CalculatePnLPercentage(
            decimal entryPrice,
            decimal quantity,
            decimal unrealizedPnl)
        {
            if (entryPrice <= 0m || quantity <= 0m)
                return 0m;

            var positionNotional =
                entryPrice * quantity;

            if (positionNotional <= 0m)
                return 0m;

            return unrealizedPnl /
                   positionNotional *
                   100m;
        }

        private static string ResolveBinancePositionDirection(
            object rawPosition,
            decimal rawQuantity)
        {
            var positionSide = GetStringProperty(
                rawPosition,
                "PositionSide");

            if (string.Equals(
                    positionSide,
                    "LONG",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Long";
            }

            if (string.Equals(
                    positionSide,
                    "SHORT",
                    StringComparison.OrdinalIgnoreCase))
            {
                return "Short";
            }

            // One-way mode normally exposes a signed position amount.
            return rawQuantity >= 0m
                ? "Long"
                : "Short";
        }

        private static string BuildPositionKey(
            string symbol,
            string direction)
        {
            return
                $"{symbol.Trim().ToUpperInvariant()}|" +
                $"{direction.Trim()}";
        }

        private static string? GetStringProperty(
            object source,
            params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                var property = source.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.IgnoreCase);

                if (property == null)
                    continue;

                var value = property.GetValue(source);

                if (value == null)
                    continue;

                return value.ToString();
            }

            return null;
        }

        private static decimal GetDecimalProperty(
            object source,
            params string[] propertyNames)
        {
            foreach (var propertyName in propertyNames)
            {
                var property = source.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.IgnoreCase);

                if (property == null)
                    continue;

                var value = property.GetValue(source);

                if (value == null)
                    continue;

                try
                {
                    return ToDecimal(value);
                }
                catch
                {
                    // Try the next known property name.
                }
            }

            return 0m;
        }

        private async Task WritePositionFailureAuditAsync(
            int userId,
            BrokerConnection connection,
            string reason)
        {
            _context.AuditLogs.Add(new AuditLog
            {
                UserId = userId,
                Action = "PositionReconciliationFailed",
                Details =
                    $"{reason} Broker: {connection.BrokerName}. " +
                    $"Environment: {(connection.IsTestnet ? "testnet" : "live")}.",
                CreatedAt = DateTime.UtcNow
            });

            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            {
                // Do not expose exchange/database internals.
            }
        }

        private async Task ReconcileBinanceSpotFillsAsync(
            BinanceRestClient client,
            ExchangeOrder order,
            long numericOrderId)
        {
            var result = await client.SpotApi.Trading.GetUserTradesAsync(
                order.Symbol,
                numericOrderId,
                null,
                null,
                100,
                null,
                null);

            if (!result.Success || result.Data == null)
                return;

            foreach (var fill in result.Data)
            {
                var fillId =
                    fill.Id.ToString(
                        CultureInfo.InvariantCulture);

                var exists = await _context.ExchangeFills.AnyAsync(f =>
                    f.ExchangeOrderId == order.Id &&
                    f.ExchangeFillId == fillId);

                if (exists)
                    continue;

                _context.ExchangeFills.Add(new ExchangeFill
                {
                    ExchangeOrderId = order.Id,
                    ExchangeFillId = fillId,
                    Quantity = ToDecimal(fill.Quantity),
                    Price = ToDecimal(fill.Price),
                    Fee = ToDecimal(fill.Fee),
                    FeeAsset = fill.FeeAsset,
                    FilledAt = fill.Timestamp,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        private async Task ReconcileBinanceFuturesFillsAsync(
            BinanceRestClient client,
            ExchangeOrder order,
            long numericOrderId)
        {
            var result =
                await client.UsdFuturesApi.Trading
                    .GetUserTradesAsync(
                        order.Symbol,
                        null,
                        null,
                        100,
                        numericOrderId,
                        null,
                        null,
                        CancellationToken.None);

            if (!result.Success || result.Data == null)
                return;

            foreach (var fill in result.Data)
            {
                var fillId =
                    fill.Id.ToString(
                        CultureInfo.InvariantCulture);

                var exists = await _context.ExchangeFills.AnyAsync(f =>
                    f.ExchangeOrderId == order.Id &&
                    f.ExchangeFillId == fillId);

                if (exists)
                    continue;

                _context.ExchangeFills.Add(new ExchangeFill
                {
                    ExchangeOrderId = order.Id,
                    ExchangeFillId = fillId,
                    Quantity = ToDecimal(fill.Quantity),
                    Price = ToDecimal(fill.Price),
                    Fee = ToDecimal(fill.Fee),
                    FeeAsset = fill.FeeAsset,
                    FilledAt = fill.Timestamp,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        private async Task ReconcileBybitFillsAsync(
            BybitRestClient client,
            ExchangeOrder order,
            Bybit.Net.Enums.Category category)
        {
            var result =
                await client.V5Api.Trading.GetUserTradesAsync(
                    category,
                    order.Symbol,
                    null,
                    order.ExchangeOrderId,
                    null,
                    null,
                    null,
                    null,
                    100,
                    null,
                    CancellationToken.None);

            if (!result.Success || result.Data?.List == null)
                return;

            foreach (var fill in result.Data.List)
            {
                var fillId = fill.TradeId;

                if (string.IsNullOrWhiteSpace(fillId))
                    continue;

                var exists = await _context.ExchangeFills.AnyAsync(f =>
                    f.ExchangeOrderId == order.Id &&
                    f.ExchangeFillId == fillId);

                if (exists)
                    continue;

                _context.ExchangeFills.Add(new ExchangeFill
                {
                    ExchangeOrderId = order.Id,
                    ExchangeFillId = fillId,
                    Quantity = ToDecimal(fill.Quantity),
                    Price = ToDecimal(fill.Price),
                    Fee = ToDecimal(fill.Fee),
                    FeeAsset = fill.FeeAsset,
                    FilledAt = fill.Timestamp,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        private void UpdateTradeFromOrder(
            ExchangeOrder order)
        {
            if (order.Trade == null)
                return;

            order.Trade.EntryPrice =
                order.AverageFillPrice > 0m
                    ? order.AverageFillPrice
                    : order.Trade.EntryPrice;

            order.Trade.Status = order.Status;
            order.Trade.UpdatedAt = DateTime.UtcNow;

            if (IsTerminalFailure(order.Status))
            {
                order.Trade.Reason =
                    order.FailureReason ??
                    $"Exchange status: {order.Status}";
            }
        }

        private async Task<bool> MarkReconciliationFailureAsync(
            ExchangeOrder order,
            string reason)
        {
            order.Status = "ReconciliationFailed";
            order.FailureReason = reason;
            order.LastReconciledAt = DateTime.UtcNow;
            order.UpdatedAt = DateTime.UtcNow;

            if (order.Trade != null)
            {
                order.Trade.Status =
                    "ReconciliationFailed";

                order.Trade.Reason = reason;
                order.Trade.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return false;
        }

        private static string NormalizeBinanceStatus(
            string status) =>
            status.ToUpperInvariant() switch
            {
                "NEW" => "New",
                "PENDING_NEW" => "Pending",
                "PARTIALLY_FILLED" => "PartiallyFilled",
                "FILLED" => "Filled",
                "CANCELED" => "Cancelled",
                "REJECTED" => "Rejected",
                "EXPIRED" => "Expired",
                _ => status
            };

        private static string NormalizeBybitStatus(
            string status) =>
            status.ToUpperInvariant() switch
            {
                "NEW" => "New",
                "PARTIALLYFILLED" => "PartiallyFilled",
                "FILLED" => "Filled",
                "CANCELLED" => "Cancelled",
                "REJECTED" => "Rejected",
                "PARTIALLYFILLED_CANCELED" => "Cancelled",
                "PARTIALLYFILLEDCANCELED" => "Cancelled",
                "DEACTIVATED" => "Cancelled",
                _ => status
            };

        private static bool IsTerminalFailure(
            string status) =>
            status is
                "Rejected" or
                "Cancelled" or
                "Expired" or
                "ReconciliationFailed";

        private static decimal ToDecimal(object? value)
        {
            if (value == null)
                return 0m;

            return Convert.ToDecimal(
                value,
                CultureInfo.InvariantCulture);
        }
    }
}

