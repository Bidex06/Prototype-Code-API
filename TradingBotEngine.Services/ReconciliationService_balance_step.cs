using Binance.Net.Clients;
using Bybit.Net.Clients;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
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
            int exchangeOrderId,
            CancellationToken cancellationToken = default)
        {
            var order = await _context.ExchangeOrders
                .Include(o => o.Trade)
                .FirstOrDefaultAsync(
                    o => o.Id == exchangeOrderId && o.UserId == userId,
                    cancellationToken);

            if (order == null || string.IsNullOrWhiteSpace(order.ExchangeOrderId))
                return false;

            var connection = await _context.BrokerConnections
                .FirstOrDefaultAsync(
                    c => c.UserId == userId &&
                         c.IsActive &&
                         c.BrokerName == order.BrokerName &&
                         c.IsTestnet == order.IsTestnet,
                    cancellationToken);

            if (connection == null)
            {
                order.FailureReason = "No active broker connection currently matches the persisted order environment.";
                order.LastReconciledAt = DateTime.UtcNow;
                order.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
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
                        await _context.SaveChangesAsync(cancellationToken);
                        return false;
                    }

                    if (order.IsFutures)
                    {
                        var result = await binance.UsdFuturesApi.Trading.GetOrderAsync(
                            order.Symbol,
                            numericOrderId,
                            null,
                            null,
                            cancellationToken);

                        if (!result.Success || result.Data == null)
                            return await MarkReconciliationFailureAsync(order, "Binance Futures order lookup failed.", cancellationToken);

                        order.Status = NormalizeBinanceStatus(result.Data.Status.ToString());
                        order.RequestedQuantity = ToDecimal(result.Data.Quantity);
                        order.FilledQuantity = ToDecimal(result.Data.QuantityFilled);
                        order.AverageFillPrice = ToDecimal(result.Data.AveragePrice);
                        order.UpdatedAt = DateTime.UtcNow;
                        order.LastReconciledAt = DateTime.UtcNow;

                        await ReconcileBinanceFuturesFillsAsync(
                            binance,
                            order,
                            numericOrderId,
                            cancellationToken);
                    }
                    else
                    {
                        var result = await binance.SpotApi.Trading.GetOrderAsync(
                            order.Symbol,
                            numericOrderId,
                            null,
                            null,
                            cancellationToken);

                        if (!result.Success || result.Data == null)
                            return await MarkReconciliationFailureAsync(order, "Binance Spot order lookup failed.", cancellationToken);

                        order.Status = NormalizeBinanceStatus(result.Data.Status.ToString());
                        order.RequestedQuantity = ToDecimal(result.Data.Quantity);
                        order.FilledQuantity = ToDecimal(result.Data.QuantityFilled);
                        order.AverageFillPrice = ToDecimal(result.Data.AverageFillPrice);
                        order.UpdatedAt = DateTime.UtcNow;
                        order.LastReconciledAt = DateTime.UtcNow;

                        await ReconcileBinanceSpotFillsAsync(
                            binance,
                            order,
                            numericOrderId,
                            cancellationToken);
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
                        cancellationToken);

                    var exchangeOrder = result.Success
                        ? result.Data?.List?.FirstOrDefault()
                        : null;

                    if (exchangeOrder == null)
                        return await MarkReconciliationFailureAsync(order, "Bybit order lookup failed.", cancellationToken);

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
                        category,
                        cancellationToken);
                }
                else
                {
                    return await MarkReconciliationFailureAsync(
                        order,
                        "Unsupported broker client.",
                        cancellationToken,
                        terminal: true);
                }

                if (string.Equals(order.OrderRole, "Entry", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateTradeFromOrder(order);

                    if (order.Trade != null &&
                        order.Trade.Status == "Filled" &&
                        (order.Trade.ProtectionStatus == "ProtectionPending" ||
                         order.Trade.ProtectionStatus == "Pending"))
                    {
                        await _brokerService.TryEnsureProtectionAsync(
                            connection,
                            order.Trade,
                            order.ExchangeOrderId,
                            order.Side);
                    }
                }
                else if (string.Equals(order.OrderRole, "StopLoss", StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(order.OrderRole, "TakeProfit", StringComparison.OrdinalIgnoreCase))
                {
                    UpdateTradeFromProtectionOrder(order);
                }

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "OrderReconciled",
                    Details = $"Exchange order {order.ExchangeOrderId} reconciled for {order.BrokerName} in {(order.IsTestnet ? "testnet" : "live")} mode. Status: {order.Status}.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                return await MarkReconciliationFailureAsync(
                    order,
                    "Exchange reconciliation failed unexpectedly.",
                    cancellationToken);
            }
        }

        public async Task<bool> ReconcileBalanceAsync(
            int userId,
            string currency = "USDT",
            bool useFutures = false,
            CancellationToken cancellationToken = default)
        {
            var connection = await _context.BrokerConnections
                .FirstOrDefaultAsync(
                    c => c.UserId == userId && c.IsActive,
                    cancellationToken);

            if (connection == null)
                return false;

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                var balance = await _brokerService.GetBalanceAsync(
                    userId,
                    currency,
                    useFutures);

                cancellationToken.ThrowIfCancellationRequested();

                if (balance < 0m)
                    return false;

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = userId,
                    Action = "BalanceReconciled",
                    Details =
                        $"Balance reconciled for {connection.BrokerName} " +
                        $"{currency} in {(connection.IsTestnet ? "testnet" : "live")} mode. " +
                        $"Account type: {(useFutures ? "futures" : "spot")}.",
                    CreatedAt = DateTime.UtcNow
                });

                await _context.SaveChangesAsync(cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
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
                    await _context.SaveChangesAsync(cancellationToken);
                }
                catch
                {
                    // Do not expose exchange/database internals.
                }

                return false;
            }
        }

        public async Task<int> ReconcilePendingOrdersAsync(
            int userId,
            CancellationToken cancellationToken = default)
        {
            var orders = await _context.ExchangeOrders
                .Where(o =>
                    o.UserId == userId &&
                    (o.Status == "Pending" ||
                     o.Status == "New" ||
                     o.Status == "NEW" ||
                     o.Status == "PartiallyFilled" ||
                     o.Status == "PendingReconciliation"))
                .Select(o => o.Id)
                .ToListAsync(cancellationToken);

            var reconciled = 0;

            foreach (var orderId in orders)
            {
                if (await ReconcileOrderAsync(userId, orderId, cancellationToken))
                    reconciled++;
            }

            return reconciled;
        }

        private async Task ReconcileBinanceSpotFillsAsync(
            BinanceRestClient client,
            ExchangeOrder order,
            long numericOrderId,
            CancellationToken cancellationToken)
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
                var fillId = fill.Id.ToString(CultureInfo.InvariantCulture);

                var exists = await _context.ExchangeFills.AnyAsync(f =>
                    f.ExchangeOrderId == order.Id &&
                    f.ExchangeFillId == fillId,
                    cancellationToken);

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
            long numericOrderId,
            CancellationToken cancellationToken)
        {
            var result = await client.UsdFuturesApi.Trading.GetUserTradesAsync(
                order.Symbol,
                null,
                null,
                100,
                numericOrderId,
                null,
                null,
                cancellationToken);

            if (!result.Success || result.Data == null)
                return;

            foreach (var fill in result.Data)
            {
                var fillId = fill.Id.ToString(CultureInfo.InvariantCulture);

                var exists = await _context.ExchangeFills.AnyAsync(f =>
                    f.ExchangeOrderId == order.Id &&
                    f.ExchangeFillId == fillId,
                    cancellationToken);

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
            Bybit.Net.Enums.Category category,
            CancellationToken cancellationToken)
        {
            var result = await client.V5Api.Trading.GetUserTradesAsync(
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
                cancellationToken);

            if (!result.Success || result.Data?.List == null)
                return;

            foreach (var fill in result.Data.List)
            {
                var fillId = fill.TradeId;

                if (string.IsNullOrWhiteSpace(fillId))
                    continue;

                var exists = await _context.ExchangeFills.AnyAsync(f =>
                    f.ExchangeOrderId == order.Id &&
                    f.ExchangeFillId == fillId,
                    cancellationToken);

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

        private void UpdateTradeFromProtectionOrder(ExchangeOrder order)
        {
            if (order.Trade == null)
                return;

            order.Trade.UpdatedAt = DateTime.UtcNow;

            if (order.Status == "Filled" && order.FilledQuantity > 0m)
            {
                order.Trade.ExitPrice = order.AverageFillPrice > 0m
                    ? order.AverageFillPrice
                    : order.Trade.ExitPrice;
                order.Trade.ExitTime = DateTime.UtcNow;
                order.Trade.Status = "Closed";
                order.Trade.Reason = order.OrderRole == "StopLoss"
                    ? "Stop-loss protection triggered."
                    : "Take-profit protection triggered.";
                order.Trade.ProtectionStatus = "Triggered";
                order.Trade.UpdatedAt = DateTime.UtcNow;

                _context.AuditLogs.Add(new AuditLog
                {
                    UserId = order.UserId,
                    Action = "ProtectionOrderTriggered",
                    Details = $"{order.OrderRole} protection order {order.ExchangeOrderId} triggered for trade {order.TradeId}.",
                    CreatedAt = DateTime.UtcNow
                });
                return;
            }

            if (IsTerminalFailure(order.Status))
            {
                order.Trade.ProtectionStatus = "ProtectionPending";
                order.Trade.Reason = order.FailureReason ?? $"Protection order status: {order.Status}";
                order.Trade.UpdatedAt = DateTime.UtcNow;
            }
        }

        private void UpdateTradeFromOrder(ExchangeOrder order)
        {
            if (order.Trade == null)
                return;

            order.Trade.EntryPrice = order.AverageFillPrice > 0m
                ? order.AverageFillPrice
                : order.Trade.EntryPrice;

            order.Trade.Status = order.Status switch
            {
                "Filled" => "Open",
                "PartiallyFilled" => "PartiallyFilled",
                "New" or "Pending" => "Pending",
                "Cancelled" or "Rejected" or "Expired" => "Cancelled",
                "ReconciliationFailed" => "ReconciliationFailed",
                _ => order.Trade.Status
            };

            if (order.Status == "Filled" && order.FilledQuantity > 0m)
                order.Trade.Quantity = order.FilledQuantity;

            order.Trade.UpdatedAt = DateTime.UtcNow;

            if (IsTerminalFailure(order.Status))
                order.Trade.Reason = order.FailureReason ?? $"Exchange status: {order.Status}";
        }

        private async Task<bool> MarkReconciliationFailureAsync(
            ExchangeOrder order,
            string reason,
            CancellationToken cancellationToken,
            bool terminal = false)
        {
            if (terminal)
                order.Status = "ReconciliationFailed";
            order.FailureReason = reason;
            order.LastReconciledAt = DateTime.UtcNow;
            order.UpdatedAt = DateTime.UtcNow;

            if (terminal && order.Trade != null)
            {
                order.Trade.Status = "ReconciliationFailed";
                order.Trade.Reason = reason;
                order.Trade.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync(cancellationToken);
            return false;
        }

        private static string NormalizeBinanceStatus(string status) => status.ToUpperInvariant() switch
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

        private static string NormalizeBybitStatus(string status) => status.ToUpperInvariant() switch
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

        private static bool IsTerminalFailure(string status) =>
            status is "Rejected" or "Cancelled" or "Expired" or "ReconciliationFailed";

        private static decimal ToDecimal(object? value)
        {
            if (value == null)
                return 0m;

            return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
        }
    }
}
