using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingBotEngine.Data;
using TradingBotEngine.Data.Models;
using TradingBotEngine.Services;
using Xunit;

namespace TradingBotEngine.Tests;

public class BrokerServiceAuditLogTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ApplicationDbContext(options);
    }

    private sealed class FakeCredentialProtector : ICredentialProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedText) => protectedText;
    }

    private static BrokerService CreateService(ApplicationDbContext context) =>
        new(context, new FakeCredentialProtector());

    private static void AddConnectedBroker(
        ApplicationDbContext context,
        int userId,
        string brokerName = "Bybit",
        bool isTestnet = true,
        bool liveTradingEnabled = false)
    {
        context.BrokerConnections.Add(new BrokerConnection
        {
            Id = 1,
            UserId = userId,
            BrokerName = brokerName,
            ApiKey = "test-key",
            ApiSecret = "test-secret",
            IsActive = true,
            IsConnected = true,
            IsTestnet = isTestnet,
            IsLiveTradingEnabled = liveTradingEnabled,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
    }

    private static void AddRiskSettings(
        ApplicationDbContext context,
        int userId,
        bool emergencyKillSwitch = false)
    {
        context.RiskSettings.Add(new RiskSetting
        {
            UserId = userId,
            RiskLevel = "Balanced",
            RiskPerTrade = 1m,
            Leverage = 1m,
            MaxOpenTrades = 5,
            DailyLossLimit = 5m,
            MaxDrawdown = 10m,
            IsEmergencyKillSwitch = emergencyKillSwitch,
            EmergencyKillSwitchActivatedAt =
                emergencyKillSwitch ? DateTime.UtcNow : null,
            UseFixedLotSize = false,
            CreatedAt = DateTime.UtcNow
        });
    }

    [Fact]
    public async Task PlaceOrderAsync_CreatesAuditLog_WhenOrderParametersAreInvalid()
    {
        await using var context = CreateContext();
        const int userId = 101;

        var service = CreateService(context);

        var result = await service.PlaceOrderAsync(
            userId,
            symbol: "",
            direction: "BUY",
            orderType: "Market",
            idempotencyKey: "audit-invalid-001");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("Invalid order parameters.");

        var audit = await context.AuditLogs.SingleAsync(a =>
            a.UserId == userId &&
            a.Action == "OrderValidationFailed");

        audit.Details.Should().Contain("Order validation failed");
        audit.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task PlaceOrderAsync_CreatesAuditLog_WhenNoBrokerConnectionExists()
    {
        await using var context = CreateContext();
        const int userId = 102;

        AddRiskSettings(context, userId);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var result = await service.PlaceOrderAsync(
            userId,
            symbol: "BTCUSDT",
            direction: "BUY",
            orderType: "Market",
            idempotencyKey: "audit-no-broker-001");

        result.Success.Should().BeFalse();
        result.Error.Should().Be("No active broker connection.");

        var audit = await context.AuditLogs.SingleAsync(a =>
            a.UserId == userId &&
            a.Action == "OrderBlocked_NoBrokerConnection");

        audit.Details.Should().Contain("no active broker connection");
    }

    [Fact]
    public async Task PlaceOrderAsync_CreatesAuditLog_WhenLiveTradingIsNotEnabled()
    {
        await using var context = CreateContext();
        const int userId = 103;

        AddConnectedBroker(
            context,
            userId,
            isTestnet: false,
            liveTradingEnabled: false);
        AddRiskSettings(context, userId);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var result = await service.PlaceOrderAsync(
            userId,
            symbol: "BTCUSDT",
            direction: "BUY",
            orderType: "Market",
            idempotencyKey: "audit-live-opt-in-001");

        result.Success.Should().BeFalse();
        result.Error.Should().Be(
            "Live trading is not explicitly enabled for this connection.");

        var audit = await context.AuditLogs.SingleAsync(a =>
            a.UserId == userId &&
            a.Action == "OrderBlocked_LiveTradingNotEnabled");

        audit.Details.Should().Contain("explicit live-trading opt-in");
    }

    [Fact]
    public async Task PlaceOrderAsync_CreatesAuditLog_WhenEmergencyKillSwitchIsActive()
    {
        await using var context = CreateContext();
        const int userId = 104;

        AddConnectedBroker(context, userId);
        AddRiskSettings(context, userId, emergencyKillSwitch: true);
        await context.SaveChangesAsync();

        var service = CreateService(context);

        var result = await service.PlaceOrderAsync(
            userId,
            symbol: "BTCUSDT",
            direction: "BUY",
            orderType: "Market",
            idempotencyKey: "audit-kill-switch-001");

        result.Success.Should().BeFalse();
        result.Error.Should().Be(
            "Trading is blocked because the emergency kill switch is active.");

        var audit = await context.AuditLogs.SingleAsync(a =>
            a.UserId == userId &&
            a.Action == "RiskBlocked_EmergencyKillSwitch");

        audit.Details.Should().Contain("emergency kill switch is active");
    }

    [Fact]
    public async Task PlaceOrderAsync_CreatesAuditLog_WhenIdempotencyKeyIsAlreadyProcessing()
    {
        await using var context = CreateContext();
        const int userId = 105;
        const string idempotencyKey = "audit-idempotency-001";

        AddConnectedBroker(context, userId);
        AddRiskSettings(context, userId);

        context.Trades.Add(new Trade
        {
            UserId = userId,
            Symbol = "BTCUSDT",
            Direction = "BUY",
            Quantity = 0.01m,
            EntryPrice = 100m,
            Status = "Pending",
            BrokerName = "Bybit",
            IdempotencyKey = idempotencyKey,
            EntryTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var service = CreateService(context);

        var result = await service.PlaceOrderAsync(
            userId,
            symbol: "BTCUSDT",
            direction: "BUY",
            orderType: "Market",
            idempotencyKey: idempotencyKey);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain(
            "already being processed");

        var audit = await context.AuditLogs.SingleAsync(a =>
            a.UserId == userId &&
            a.Action == "OrderBlocked_IdempotencyConflict");

        audit.Details.Should().Contain(idempotencyKey);
    }

    [Fact]
    public async Task PlaceOrderAsync_CreatesAuditLog_WhenIdempotencyRequestReplaysExistingOrder()
    {
        await using var context = CreateContext();
        const int userId = 106;
        const string idempotencyKey = "audit-idempotency-replay-001";

        AddConnectedBroker(context, userId);
        AddRiskSettings(context, userId);

        context.Trades.Add(new Trade
        {
            UserId = userId,
            Symbol = "BTCUSDT",
            Direction = "BUY",
            Quantity = 0.01m,
            EntryPrice = 100m,
            OrderId = "existing-order-106",
            Status = "Open",
            BrokerName = "Bybit",
            IdempotencyKey = idempotencyKey,
            EntryTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var service = CreateService(context);

        var result = await service.PlaceOrderAsync(
            userId,
            symbol: "BTCUSDT",
            direction: "BUY",
            orderType: "Market",
            idempotencyKey: idempotencyKey);

        result.Success.Should().BeTrue();
        result.OrderId.Should().Be("existing-order-106");

        var audit = await context.AuditLogs.SingleAsync(a =>
            a.UserId == userId &&
            a.Action == "OrderIdempotencyReplay");

        audit.Details.Should().Contain("existing-order-106");
        audit.Details.Should().Contain(idempotencyKey);
    }

    [Fact]
    public async Task DisconnectAsync_CreatesAuditLog_WhenBrokerIsDisconnected()
    {
        await using var context = CreateContext();
        const int userId = 107;

        AddConnectedBroker(context, userId);
        await context.SaveChangesAsync();

        var service = CreateService(context);

       var result = await service.DisconnectAsync(userId, 1);

        result.Should().BeTrue();

        var audit = await context.AuditLogs.SingleAsync(a =>
            a.UserId == userId &&
            a.Action == "BrokerDisconnected");

        audit.Details.Should().Contain("Bybit");
        audit.Details.Should().Contain("connection 1");
    }
}
