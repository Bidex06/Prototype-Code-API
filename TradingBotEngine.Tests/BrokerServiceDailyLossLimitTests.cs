using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingBotEngine.Data;
using TradingBotEngine.Data.Models;
using TradingBotEngine.Services;
using Xunit;

namespace TradingBotEngine.Tests;

public class BrokerServiceDailyLossLimitTests
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
            return Task.FromResult(100m);
        }
    }

    [Fact]
    public async Task DailyLossLimit_BlocksOrder_WhenTodaysRealizedLossReachesLimit()
    {
        await using var context = CreateContext();

        const int userId = 6;

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
            UseFixedLotSize = false,
            CreatedAt = DateTime.UtcNow
        });

        context.Trades.Add(new Trade
        {
            UserId = userId,
            Symbol = "BTCUSDT",
            Direction = "BUY",
            Quantity = 1m,
            EntryPrice = 100m,
            ExitPrice = 94m,
            ProfitLoss = -6m,
            Status = "Closed",
            BrokerName = "Bybit",
            EntryTime = DateTime.UtcNow.AddHours(-2),
            ExitTime = DateTime.UtcNow.AddHours(-1),
            CreatedAt = DateTime.UtcNow.AddHours(-2),
            UpdatedAt = DateTime.UtcNow.AddHours(-1)
        });

        await context.SaveChangesAsync();

        var service = new TestBrokerService(context);

        var result = await service.PlaceOrderAsync(
            userId: userId,
            symbol: "BTCUSDT",
            direction: "BUY",
            orderType: "Market",
            idempotencyKey: "daily-loss-test-001");

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Daily loss limit reached");

        var audit = await context.AuditLogs
            .Where(a =>
                a.UserId == userId &&
                a.Action == "RiskBlocked_DailyLossLimit")
            .ToListAsync();

        audit.Should().ContainSingle();

        var trades = await context.Trades
            .Where(t => t.UserId == userId)
            .ToListAsync();

        trades.Should().HaveCount(1);
    }
}
