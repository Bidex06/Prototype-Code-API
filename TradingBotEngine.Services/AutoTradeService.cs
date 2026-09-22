using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using TradingBotEngine.Core;
using TradingBotEngine.Core.Models;
using TradingBotEngine.Data;
using TradingBotEngine.Data.Models;

namespace TradingBotEngine.Services;

/// <summary>
/// P1.6D automated execution pipeline.
///
/// The worker is responsible for orchestration only:
/// bot eligibility -> closed market candle -> indicators -> signal -> execution request.
/// BrokerService remains the single authority for server-side sizing, risk controls,
/// exchange rules, idempotency and exchange submission.
/// </summary>
public sealed class AutoTradeService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutoTradeService> _logger;

    private static readonly TimeSpan WorkerInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ReconciliationInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MarketDataTimeout = TimeSpan.FromSeconds(20);
    private const int CandleLimit = 250;

    // Prevents re-processing the same closed candle repeatedly during one process lifetime.
    // BrokerService idempotency remains the durable safety net across restarts.
    private readonly Dictionary<string, DateTime> _lastProcessedCandle = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _processedCandleLock = new();
    private const int MaxMarketDataAttempts = 3;
    private DateTime _lastReconciliationAt = DateTime.MinValue;
    private long _completedCycles;
    private long _failedCycles;
    private static readonly TimeSpan[] MarketDataRetryDelays =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    };

    public AutoTradeService(
        IServiceProvider serviceProvider,
        ILogger<AutoTradeService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "AutoTradeService started. Interval={IntervalSeconds}s, reconciliation interval={ReconciliationIntervalSeconds}s.",
            WorkerInterval.TotalSeconds,
            ReconciliationInterval.TotalSeconds);

        // PeriodicTimer prevents overlapping cycles by design: the next tick is
        // awaited only after the current cycle has completely finished.
        using var timer = new PeriodicTimer(WorkerInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            var cycleStartedAt = DateTime.UtcNow;

            try
            {
                await ProcessRunningBotsAsync(stoppingToken);
                await MaybeRunReconciliationAsync(stoppingToken);

                _completedCycles++;
                _logger.LogInformation(
                    "AutoTradeService cycle completed. DurationMs={DurationMs}, CompletedCycles={CompletedCycles}, FailedCycles={FailedCycles}.",
                    (DateTime.UtcNow - cycleStartedAt).TotalMilliseconds,
                    _completedCycles,
                    _failedCycles);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never allow an unexpected worker dependency failure to stop
                // the ASP.NET host. Bot/symbol failures are already isolated
                // below, while this is the final cycle-level safety net.
                _failedCycles++;
                _logger.LogError(
                    ex,
                    "AutoTradeService cycle failed but the worker will continue. DurationMs={DurationMs}, CompletedCycles={CompletedCycles}, FailedCycles={FailedCycles}.",
                    (DateTime.UtcNow - cycleStartedAt).TotalMilliseconds,
                    _completedCycles,
                    _failedCycles);
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                    break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation(
            "AutoTradeService stopped. CompletedCycles={CompletedCycles}, FailedCycles={FailedCycles}.",
            _completedCycles,
            _failedCycles);
    }

    private async Task ProcessRunningBotsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var marketData = scope.ServiceProvider.GetRequiredService<ExchangeMarketDataService>();
        var indicatorCalculator = scope.ServiceProvider.GetRequiredService<IndicatorCalculator>();
        var signalGenerator = scope.ServiceProvider.GetRequiredService<SignalGenerator>();
        var brokerService = scope.ServiceProvider.GetRequiredService<BrokerService>();

        var bots = await db.TradingBots
            .Include(b => b.User)
                .ThenInclude(u => u.Subscription)
            .Include(b => b.User)
                .ThenInclude(u => u.RiskSetting)
            .Include(b => b.BrokerConnection)
            .Include(b => b.TrackedSymbols)
                .ThenInclude(bs => bs.TrackedSymbol)
            .Where(b => b.IsEnabled && b.IsRunning)
            .ToListAsync(cancellationToken);

        _logger.LogInformation(
            "P1.6D worker cycle found {BotCount} enabled/running bot(s).",
            bots.Count);

        if (bots.Count == 0)
        {
            _logger.LogDebug("No enabled and running trading bots are currently configured.");
            return;
        }

        foreach (var bot in bots)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ProcessBotAsync(
                    bot,
                    marketData,
                    indicatorCalculator,
                    signalGenerator,
                    brokerService,
                    db,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Isolate one broken bot from every other running bot.
                _logger.LogError(
                    ex,
                    "Auto-trade processing failed for bot {BotId} ({BotName}), user {UserId}.",
                    bot.Id,
                    bot.Name,
                    bot.UserId);
            }
        }
    }

    private async Task ProcessBotAsync(
        TradingBot bot,
        ExchangeMarketDataService marketData,
        IndicatorCalculator indicatorCalculator,
        SignalGenerator signalGenerator,
        BrokerService brokerService,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "P1.6D processing bot {BotId} ({BotName}), user {UserId}.",
            bot.Id,
            bot.Name,
            bot.UserId);

        var eligibility = EvaluateEligibility(bot);

        if (!eligibility.IsEligible)
        {
            _logger.LogWarning(
                "Auto-trade blocked for bot {BotId} ({BotName}), user {UserId}: {Reason}",
                bot.Id,
                bot.Name,
                bot.UserId,
                eligibility.Reason);
            return;
        }

        var broker = bot.BrokerConnection!;
        var enabledSymbols = bot.TrackedSymbols
            .Where(x => x.TrackedSymbol != null && x.TrackedSymbol.IsEnabled)
            .Select(x => x.TrackedSymbol!)
            .ToList();

        _logger.LogInformation(
            "P1.6D bot {BotId} eligible. Broker={Broker}, Testnet={IsTestnet}, Futures={UseFutures}, Symbols={SymbolCount}.",
            bot.Id,
            broker.BrokerName,
            broker.IsTestnet,
            bot.UseFutures,
            enabledSymbols.Count);

        foreach (var trackedSymbol in enabledSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _logger.LogInformation(
                "P1.6D starting symbol processing for bot {BotId}, symbol {Symbol}, timeframe {Timeframe}.",
                bot.Id,
                trackedSymbol.Symbol,
                bot.Timeframe);

            try
            {
                await ProcessSymbolAsync(
                    bot,
                    broker,
                    trackedSymbol.Symbol,
                    marketData,
                    indicatorCalculator,
                    signalGenerator,
                    brokerService,
                    db,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Auto-trade symbol processing failed for bot {BotId}, symbol {Symbol}.",
                    bot.Id,
                    trackedSymbol.Symbol);
            }
        }
    }

    private async Task ProcessSymbolAsync(
        TradingBot bot,
        BrokerConnection broker,
        string symbol,
        ExchangeMarketDataService marketData,
        IndicatorCalculator indicatorCalculator,
        SignalGenerator signalGenerator,
        BrokerService brokerService,
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "P1.6D fetching {CandleLimit} candles for bot {BotId}, {Symbol}.",
            CandleLimit,
            bot.Id,
            symbol);

        var candles = await GetCandlesWithRetryAsync(
            marketData,
            broker.BrokerName,
            symbol,
            bot.Timeframe,
            bot.UseFutures,
            broker.IsTestnet,
            cancellationToken);

        _logger.LogInformation(
            "P1.6D market data returned {CandleCount} candles for bot {BotId}, {Symbol}.",
            candles.Count,
            bot.Id,
            symbol);

        var closedCandles = candles
            .Where(c => IsCandleClosed(c.Timestamp, bot.Timeframe))
            .OrderBy(c => c.Timestamp)
            .ToList();

        if (closedCandles.Count < 200)
        {
            _logger.LogDebug(
                "Bot {BotId} skipped {Symbol}: only {CandleCount} closed candles are available; 200 are required.",
                bot.Id,
                symbol,
                closedCandles.Count);
            return;
        }

        var latestClosedCandle = closedCandles[^1];

        _logger.LogInformation(
            "P1.6D found {ClosedCandleCount} closed candles for bot {BotId}, {Symbol}. Latest closed candle={CandleTime:O}.",
            closedCandles.Count,
            bot.Id,
            symbol,
            latestClosedCandle.Timestamp);

        var processKey = BuildProcessKey(bot.Id, symbol, bot.Timeframe);

        lock (_processedCandleLock)
        {
            if (_lastProcessedCandle.TryGetValue(processKey, out var lastProcessed) &&
                latestClosedCandle.Timestamp <= lastProcessed)
            {
                _logger.LogInformation(
                    "P1.6D skipped bot {BotId}, {Symbol}: candle {CandleTime:O} was already processed in this process (last={LastProcessed:O}).",
                    bot.Id,
                    symbol,
                    latestClosedCandle.Timestamp,
                    lastProcessed);
                return;
            }
        }

        _logger.LogInformation(
            "P1.6D calculating indicators for bot {BotId}, {Symbol}.",
            bot.Id,
            symbol);

        var indicatorInput = closedCandles;
        cancellationToken.ThrowIfCancellationRequested();
        var indicators = await indicatorCalculator.CalculateIndicators(indicatorInput);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "P1.6D indicators calculated for bot {BotId}, {Symbol}. IndicatorCount={IndicatorCount}.",
            bot.Id,
            symbol,
            indicators.Count);

        _logger.LogInformation(
            "P1.6D generating signal for bot {BotId}, {Symbol}.",
            bot.Id,
            symbol);

        cancellationToken.ThrowIfCancellationRequested();
        var signal = await signalGenerator.GenerateSignal(
            symbol,
            indicatorInput,
            indicators);
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogInformation(
            "P1.6D signal generated for bot {BotId}, {Symbol}: Action={Action}, Price={Price}, SL={StopLoss}, TP={TakeProfit}, Confidence={Confidence}.",
            bot.Id,
            symbol,
            signal.Action,
            signal.Price,
            signal.StopLoss,
            signal.TakeProfit,
            signal.Confidence);

        if (signal.Action.Equals("HOLD", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "P1.6D bot {BotId} generated HOLD for {Symbol} on candle {CandleTime:O}. No order will be submitted.",
                bot.Id,
                symbol,
                latestClosedCandle.Timestamp);

            MarkCandleProcessed(processKey, latestClosedCandle.Timestamp);
            return;
        }

        if (!IsValidExecutableSignal(signal))
        {
            await WriteAuditAsync(
                db,
                bot.UserId,
                "AutoTradeSignalBlocked",
                $"Bot {bot.Id} generated an invalid executable {signal.Action} signal for {symbol}. " +
                "A valid protective stop-loss and take-profit are required before automated execution.",
                cancellationToken);

            _logger.LogWarning(
                "Bot {BotId} signal blocked for {Symbol}: invalid protective levels or signal values.",
                bot.Id,
                symbol);

            MarkCandleProcessed(processKey, latestClosedCandle.Timestamp);
            return;
        }

        var idempotencyKey = BuildIdempotencyKey(
            bot.Id,
            symbol,
            bot.Timeframe,
            latestClosedCandle.Timestamp);

        await WriteAuditAsync(
            db,
            bot.UserId,
            "AutoTradeExecutionAttempt",
            $"Bot {bot.Id} generated {signal.Action} for {symbol} at {latestClosedCandle.Timestamp:O}. " +
            $"Broker={broker.BrokerName}, Testnet={broker.IsTestnet}, Futures={bot.UseFutures}, IdempotencyKey={idempotencyKey}.",
            cancellationToken);

        var isSpot = !bot.UseFutures;
        var isSpotSell = isSpot && signal.Action.Equals("SELL", StringComparison.OrdinalIgnoreCase);

        // Do not cancel an exchange submission once it has started. If the
        // network call is interrupted, its outcome may be unknown and the
        // BrokerService idempotency/reconciliation path must remain authoritative.
        cancellationToken.ThrowIfCancellationRequested();

        var result = await brokerService.PlaceOrderAsync(
            userId: bot.UserId,
            symbol: symbol,
            direction: signal.Action,
            orderType: isSpot ? "Limit" : "Market",
            stopLoss: isSpotSell ? null : signal.StopLoss,
            takeProfit: isSpotSell ? null : signal.TakeProfit,
            entryPrice: isSpot ? signal.Price : null,
            useFutures: bot.UseFutures,
            idempotencyKey: idempotencyKey);

        cancellationToken.ThrowIfCancellationRequested();

        var trade = await db.Trades
            .FirstOrDefaultAsync(
                t => t.UserId == bot.UserId && t.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (result.Success)
        {
            if (trade != null)
            {
                var persistedSignal = await PersistSignalAsync(
                    db,
                    bot.UserId,
                    signal,
                    wasExecuted: true,
                    tradeId: trade.Id,
                    cancellationToken: cancellationToken);

                await WriteAuditAsync(
                    db,
                    bot.UserId,
                    "AutoTradeExecutionAccepted",
                    $"Bot {bot.Id} execution accepted for {symbol}. ExchangeOrderId={result.OrderId ?? "unknown"}, TradeId={trade.Id}, SignalId={persistedSignal.Id}.",
                    cancellationToken);
            }
            else
            {
                await PersistSignalAsync(
                    db,
                    bot.UserId,
                    signal,
                    wasExecuted: false,
                    tradeId: null,
                    cancellationToken: cancellationToken);

                await WriteAuditAsync(
                    db,
                    bot.UserId,
                    "AutoTradeExecutionPersistenceWarning",
                    $"Bot {bot.Id} received an accepted execution result for {symbol}, but no local trade was found for idempotency key '{idempotencyKey}'. Reconciliation is required.",
                    cancellationToken);
            }

            _logger.LogInformation(
                "Bot {BotId} execution accepted for {Symbol}. ExchangeOrderId={OrderId}.",
                bot.Id,
                symbol,
                result.OrderId);

            MarkCandleProcessed(processKey, latestClosedCandle.Timestamp);
            return;
        }

        await PersistSignalAsync(
            db,
            bot.UserId,
            signal,
            wasExecuted: false,
            tradeId: trade?.Id,
            cancellationToken: cancellationToken);

        await WriteAuditAsync(
            db,
            bot.UserId,
            "AutoTradeExecutionBlocked",
            $"Bot {bot.Id} execution was not accepted for {symbol}. Reason: {result.Error ?? "Unknown execution error"}. No retry was issued automatically because the BrokerService result may represent a reserved or unresolved exchange request.",
            cancellationToken);

        _logger.LogWarning(
            "Bot {BotId} execution was not accepted for {Symbol}: {Error}",
            bot.Id,
            symbol,
            result.Error ?? "Unknown execution error");

        MarkCandleProcessed(processKey, latestClosedCandle.Timestamp);
    }

    private async Task<IReadOnlyList<Candle>> GetCandlesWithRetryAsync(
        ExchangeMarketDataService marketData,
        string brokerName,
        string symbol,
        string timeframe,
        bool useFutures,
        bool isTestnet,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= MaxMarketDataAttempts; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(MarketDataTimeout);

                return await marketData.GetCandlesAsync(
                    brokerName,
                    symbol,
                    timeframe,
                    useFutures,
                    CandleLimit,
                    isTestnet);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastException = new TimeoutException("Market-data request timed out.");
            }
            catch (Exception ex) when (IsTransientMarketDataException(ex))
            {
                lastException = ex;
            }

            if (attempt == MaxMarketDataAttempts)
                break;

            var delay = MarketDataRetryDelays[attempt - 1];
            _logger.LogWarning(
                "P1.6E transient market-data failure for {Broker} {Symbol} {Timeframe}. Retry {Attempt}/{MaxAttempts} in {DelaySeconds}s.",
                brokerName,
                symbol,
                timeframe,
                attempt,
                MaxMarketDataAttempts,
                delay.TotalSeconds);

            await Task.Delay(delay, cancellationToken);
        }

        throw new InvalidOperationException(
            $"Market-data retrieval failed after {MaxMarketDataAttempts} attempts for {brokerName} {symbol} {timeframe}.",
            lastException);
    }

    private static bool IsTransientMarketDataException(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is TimeoutException ||
                current is HttpRequestException ||
                current is SocketException ||
                current is IOException)
            {
                return true;
            }

            // Exchange libraries commonly surface unsuccessful HTTP/API calls
            // as InvalidOperationException after converting their result object.
            if (current is InvalidOperationException &&
                current.Message.Contains("Unable to get", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void MarkCandleProcessed(string processKey, DateTime candleTimestamp)
    {
        lock (_processedCandleLock)
        {
            _lastProcessedCandle[processKey] = candleTimestamp;
        }
    }

    private static bool IsValidExecutableSignal(TradingSignal signal)
    {
        if (signal == null ||
            signal.Price <= 0m ||
            signal.StopLoss is null ||
            signal.TakeProfit is null ||
            signal.StopLoss <= 0m ||
            signal.TakeProfit <= 0m)
        {
            return false;
        }

        if (signal.Action.Equals("BUY", StringComparison.OrdinalIgnoreCase))
        {
            return signal.StopLoss.Value < signal.Price &&
                   signal.TakeProfit.Value > signal.Price;
        }

        if (signal.Action.Equals("SELL", StringComparison.OrdinalIgnoreCase))
        {
            return signal.StopLoss.Value > signal.Price &&
                   signal.TakeProfit.Value < signal.Price;
        }

        return false;
    }

    private static async Task<Signal> PersistSignalAsync(
        ApplicationDbContext db,
        int userId,
        TradingSignal signal,
        bool wasExecuted,
        int? tradeId,
        CancellationToken cancellationToken)
    {
        var entity = new Signal
        {
            UserId = userId,
            Symbol = signal.Symbol,
            Action = signal.Action.ToUpperInvariant(),
            Price = signal.Price,
            StopLoss = signal.StopLoss,
            TakeProfit = signal.TakeProfit,
            Confidence = signal.Confidence,
            Pattern = signal.Pattern,
            Rsi = signal.Rsi,
            Trend = signal.Trend,
            Support = signal.Support,
            Resistance = signal.Resistance,
            Indicators = signal.Indicators,
            Reason = signal.Reason,
            GeneratedAt = signal.GeneratedAt,
            WasExecuted = wasExecuted,
            ExecutedAt = wasExecuted ? DateTime.UtcNow : null,
            TradeId = tradeId
        };

        db.Signals.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    private static async Task WriteAuditAsync(
        ApplicationDbContext db,
        int userId,
        string action,
        string details,
        CancellationToken cancellationToken)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            Details = details,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MaybeRunReconciliationAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (now - _lastReconciliationAt < ReconciliationInterval)
            return;

        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var reconciliation = scope.ServiceProvider.GetRequiredService<IReconciliationService>();

        var userIds = await db.ExchangeOrders
            .AsNoTracking()
            .Where(o =>
                o.Status == "Pending" ||
                o.Status == "New" ||
                o.Status == "NEW" ||
                o.Status == "PartiallyFilled" ||
                o.Status == "PendingReconciliation")
            .Select(o => o.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Only advance the schedule after the database query succeeded. A
        // transient database outage must not consume the reconciliation window.
        _lastReconciliationAt = now;

        if (userIds.Count == 0)
        {
            _logger.LogDebug("Reconciliation schedule reached: no pending exchange orders found.");
            return;
        }

        _logger.LogInformation(
            "Starting scheduled reconciliation for {UserCount} user(s).",
            userIds.Count);

        foreach (var userId in userIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var reconciled = await reconciliation.ReconcilePendingOrdersAsync(
                    userId,
                    cancellationToken);

                _logger.LogInformation(
                    "Scheduled reconciliation completed for user {UserId}. ReconciledOrders={ReconciledOrders}.",
                    userId,
                    reconciled);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One user's exchange/database problem must not prevent other
                // users from being reconciled on the same cycle.
                _logger.LogError(
                    ex,
                    "Scheduled reconciliation failed for user {UserId}; other users will continue.",
                    userId);
            }
        }
    }

    private static string BuildProcessKey(
        int botId,
        string symbol,
        string timeframe) =>
        $"{botId}:{symbol.Trim().ToUpperInvariant()}:{timeframe.Trim().ToLowerInvariant()}";

    private static string BuildIdempotencyKey(
        int botId,
        string symbol,
        string timeframe,
        DateTime candleTimestamp) =>
        $"auto:{botId}:{symbol.Trim().ToUpperInvariant()}:{timeframe.Trim().ToLowerInvariant()}:{candleTimestamp.ToUniversalTime():yyyyMMddHHmmss}";

    private static bool IsCandleClosed(DateTime candleOpenTime, string timeframe)
    {
        var duration = GetTimeframeDuration(timeframe);
        return candleOpenTime.ToUniversalTime().Add(duration) <= DateTime.UtcNow;
    }

    private static TimeSpan GetTimeframeDuration(string timeframe) =>
        timeframe.Trim().ToLowerInvariant() switch
        {
            "1m" => TimeSpan.FromMinutes(1),
            "3m" => TimeSpan.FromMinutes(3),
            "5m" => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "30m" => TimeSpan.FromMinutes(30),
            "1h" => TimeSpan.FromHours(1),
            "2h" => TimeSpan.FromHours(2),
            "4h" => TimeSpan.FromHours(4),
            "6h" => TimeSpan.FromHours(6),
            "8h" => TimeSpan.FromHours(8),
            "12h" => TimeSpan.FromHours(12),
            "1d" => TimeSpan.FromDays(1),
            "3d" => TimeSpan.FromDays(3),
            "1w" => TimeSpan.FromDays(7),
            _ => throw new ArgumentException($"Unsupported timeframe '{timeframe}'.", nameof(timeframe))
        };

    private static BotEligibilityResult EvaluateEligibility(TradingBot bot)
    {
        if (bot.User == null)
            return BotEligibilityResult.Blocked("user record is unavailable");

        if (!bot.User.IsActive)
            return BotEligibilityResult.Blocked("user account is inactive");

        // Keep the existing user-level switch as a global safety gate.
        if (!bot.User.IsAutoTradeEnabled)
            return BotEligibilityResult.Blocked("user-level auto-trading is disabled");

        var subscription = bot.User.Subscription;
        if (subscription == null ||
            !subscription.IsActive ||
            subscription.EndDate <= DateTime.UtcNow)
        {
            return BotEligibilityResult.Blocked("subscription is inactive or expired");
        }

        var riskSetting = bot.User.RiskSetting;
        if (riskSetting == null)
            return BotEligibilityResult.Blocked("risk settings are not configured");

        if (!IsSupportedRiskLevel(riskSetting.RiskLevel))
            return BotEligibilityResult.Blocked($"unsupported risk level '{riskSetting.RiskLevel}'");

        if (riskSetting.IsEmergencyKillSwitch)
            return BotEligibilityResult.Blocked("emergency kill switch is active");

        if (bot.BrokerConnection == null)
            return BotEligibilityResult.Blocked("no broker connection is assigned");

        var broker = bot.BrokerConnection;

        if (!broker.IsActive)
            return BotEligibilityResult.Blocked("assigned broker connection is inactive");

        if (!broker.IsConnected)
            return BotEligibilityResult.Blocked("assigned broker connection is not connected");

        if (!IsSupportedBroker(broker.BrokerName))
            return BotEligibilityResult.Blocked($"unsupported broker '{broker.BrokerName}'");

        if (!broker.IsTestnet && !broker.IsLiveTradingEnabled)
            return BotEligibilityResult.Blocked("live trading is not explicitly enabled for the assigned broker connection");

        if (bot.TrackedSymbols == null || bot.TrackedSymbols.Count == 0)
            return BotEligibilityResult.Blocked("bot has no tracked symbols assigned");

        var enabledSymbols = bot.TrackedSymbols
            .Where(x => x.TrackedSymbol != null && x.TrackedSymbol.IsEnabled)
            .Select(x => x.TrackedSymbol!)
            .ToList();

        if (enabledSymbols.Count == 0)
            return BotEligibilityResult.Blocked("bot has no enabled tracked symbols");

        var mismatchedSymbol = enabledSymbols.FirstOrDefault(symbol =>
            !symbol.Exchange.Equals(broker.BrokerName, StringComparison.OrdinalIgnoreCase));

        if (mismatchedSymbol != null)
        {
            return BotEligibilityResult.Blocked(
                $"tracked symbol '{mismatchedSymbol.Symbol}' belongs to exchange '{mismatchedSymbol.Exchange}', not broker '{broker.BrokerName}'");
        }

        return BotEligibilityResult.Allowed(
            enabledSymbols.Select(x => x.Symbol).ToArray());
    }

    private static bool IsSupportedRiskLevel(string? riskLevel) =>
        riskLevel != null &&
        (riskLevel.Equals("Conservative", StringComparison.OrdinalIgnoreCase) ||
         riskLevel.Equals("Balanced", StringComparison.OrdinalIgnoreCase) ||
         riskLevel.Equals("Aggressive", StringComparison.OrdinalIgnoreCase));

    private static bool IsSupportedBroker(string brokerName) =>
        brokerName.Equals("Binance", StringComparison.OrdinalIgnoreCase) ||
        brokerName.Equals("Bybit", StringComparison.OrdinalIgnoreCase);

    private sealed record BotEligibilityResult(
        bool IsEligible,
        string Reason,
        IReadOnlyList<string> EnabledSymbols)
    {
        public static BotEligibilityResult Allowed(IReadOnlyList<string> symbols) =>
            new(true, string.Empty, symbols);

        public static BotEligibilityResult Blocked(string reason) =>
            new(false, reason, Array.Empty<string>());
    }
}
