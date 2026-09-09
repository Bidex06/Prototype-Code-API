using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TradingBotEngine.Data;
using TradingBotEngine.Data.Models;
using TradingBotEngine.Services;
using Xunit;

namespace TradingBotEngine.Tests;

public class BrokerServiceIdempotencyTests
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
    public async Task Same_IdempotencyKey_WithExistingOrderId_ReturnsExistingOrder()
    {
        await using var context = CreateContext();

        const int userId = 6;
        const string idempotencyKey = "idem-existing-001";
        const string existingOrderId = "existing-order-123";

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

        context.Trades.Add(new Trade
        {
            UserId = userId,
            Symbol = "BTCUSDT",
            Direction = "BUY",
            Quantity = 1m,
            EntryPrice = 100m,
            Status = "Open",
            BrokerName = "Bybit",
            IdempotencyKey = idempotencyKey,
            OrderId = existingOrderId,
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
            idempotencyKey: idempotencyKey);

        result.Success.Should().BeTrue();
        result.OrderId.Should().Be(existingOrderId);

        var matchingTrades = await context.Trades
            .Where(t =>
                t.UserId == userId &&
                t.IdempotencyKey == idempotencyKey)
            .ToListAsync();

        matchingTrades.Should().HaveCount(1);
    }

    [Fact]
    public async Task Same_IdempotencyKey_WithoutOrderId_BlocksRetry()
    {
        await using var context = CreateContext();

        const int userId = 6;
        const string idempotencyKey = "idem-pending-001";

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

        context.Trades.Add(new Trade
        {
            UserId = userId,
            Symbol = "BTCUSDT",
            Direction = "BUY",
            Quantity = 1m,
            EntryPrice = 100m,
            Status = "PendingReconciliation",
            BrokerName = "Bybit",
            IdempotencyKey = idempotencyKey,
            OrderId = null,
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
            idempotencyKey: idempotencyKey);

        result.Success.Should().BeFalse();

        result.Error.Should().Contain(
            "Reconciliation is required");

        var matchingTrades = await context.Trades
            .Where(t =>
                t.UserId == userId &&
                t.IdempotencyKey == idempotencyKey)
            .ToListAsync();

        matchingTrades.Should().HaveCount(1);
    }
}