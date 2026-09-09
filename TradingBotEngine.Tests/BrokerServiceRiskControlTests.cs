using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingBotEngine.Data;
using TradingBotEngine.Data.Models;
using TradingBotEngine.Services;
using Xunit;

namespace TradingBotEngine.Tests;

public class BrokerServiceRiskControlTests
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

    [Fact]
    public async Task PlaceOrderAsync_BlocksOrder_WhenMaximumOpenTradesIsReached()
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
            MaxOpenTrades = 1,
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
            Quantity = 0.01m,
            EntryPrice = 100m,
            Status = "Open",
            BrokerName = "Bybit",
            EntryTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await context.SaveChangesAsync();

        var service = new BrokerService(
            context,
            new FakeCredentialProtector());

        var result = await service.PlaceOrderAsync(
            userId: userId,
            symbol: "BTCUSDT",
            direction: "BUY",
            orderType: "Market",
            idempotencyKey: "max-open-trades-001");

        result.Success.Should().BeFalse();
        result.Error.Should().Be(
            "Maximum open trades limit reached (1).");

        var auditLog = await context.AuditLogs
            .SingleOrDefaultAsync(a =>
                a.UserId == userId &&
                a.Action == "RiskBlocked_MaxOpenTrades");

        auditLog.Should().NotBeNull();
        auditLog!.Details.Should().Contain(
            "maximum number of open trades (1)");
        auditLog.Details.Should().Contain(
            "Current active/unresolved trades: 1.");

        var trades = await context.Trades
            .Where(t => t.UserId == userId)
            .ToListAsync();

        trades.Should().HaveCount(1);
    }
}
