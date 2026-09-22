using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingBotEngine.Data;
using TradingBotEngine.Data.Models;
using TradingBotEngine.Services;
using Xunit;

namespace TradingBotEngine.Tests;

public class BrokerServiceEmergencyKillSwitchTests
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

    private sealed class TestBrokerService : BrokerService
    {
        public bool ExchangeDependentPathReached { get; private set; }

        public TestBrokerService(ApplicationDbContext context)
            : base(context, new FakeCredentialProtector())
        {
        }

        public override Task<decimal> GetBalanceAsync(
            int userId,
            string currency = "USDT",
            bool useFutures = false,
            int? connectionId = null)
        {
            ExchangeDependentPathReached = true;

            throw new InvalidOperationException(
                "Exchange-dependent execution was reached while the emergency kill switch was active.");
        }
    }

    [Fact]
    public async Task EmergencyKillSwitch_BlocksOrder_BeforeExchangeDependentExecution()
    {
        await using var context = CreateContext();

        const int userId = 8;

        context.BrokerConnections.Add(new BrokerConnection
        {
            Id = 1,
            UserId = userId,
            BrokerName = "Bybit",
            ApiKey = "test-key",
            ApiSecret = "test-secret",
            IsActive = true,
            IsConnected = true,
            IsTestnet = true,
            IsLiveTradingEnabled = false
        });

        context.RiskSettings.Add(new RiskSetting
        {
            UserId = userId,
            RiskLevel = "Balanced",
            RiskPerTrade = 1m,
            Leverage = 1m,
            MaxOpenTrades = 5,
            DailyLossLimit = 5m,
            MaxDrawdown = 10m,
            IsEmergencyKillSwitch = true,
            EmergencyKillSwitchActivatedAt = DateTime.UtcNow,
            UseFixedLotSize = false,
            CreatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var service = new TestBrokerService(context);

        var result = await service.PlaceOrderAsync(
            userId: userId,
            symbol: "BTCUSDT",
            direction: "BUY",
            orderType: "Market",
            idempotencyKey: "emergency-kill-switch-test-001");

        result.Success.Should().BeFalse();

        result.Error.Should().Contain(
            "emergency kill switch is active");

        service.ExchangeDependentPathReached.Should().BeFalse();

        var audit = await context.AuditLogs
            .Where(a =>
                a.UserId == userId &&
                a.Action == "RiskBlocked_EmergencyKillSwitch")
            .ToListAsync();

        audit.Should().ContainSingle();

        var trades = await context.Trades
            .Where(t => t.UserId == userId)
            .ToListAsync();

        trades.Should().BeEmpty();
    }
}