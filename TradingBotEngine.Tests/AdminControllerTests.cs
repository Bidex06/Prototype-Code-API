using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradingBotEngine.API.Controllers;
using TradingBotEngine.Data;
using TradingBotEngine.Data.Models;

namespace TradingBotEngine.Tests;

public class AdminControllerTests
{
    [Fact]
    public async Task GetUsers_ReturnsOperationalCounts_WithoutSecrets()
    {
        await using var context = CreateContext();
        var user = new User
        {
            Id = 10,
            Email = "user@example.com",
            FirstName = "Test",
            LastName = "User",
            Role = "User",
            IsActive = true,
            IsAutoTradeEnabled = true
        };
        context.Users.Add(user);
        context.Subscriptions.Add(new Subscription
        {
            Id = 20,
            UserId = user.Id,
            Plan = "Monthly",
            StartDate = DateTime.UtcNow.AddDays(-5),
            EndDate = DateTime.UtcNow.AddDays(25),
            IsActive = true,
            IsTrial = false
        });
        context.TradingBots.AddRange(
            new TradingBot { Id = 30, UserId = user.Id, Name = "Running", Strategy = "S", IsEnabled = true, IsRunning = true },
            new TradingBot { Id = 31, UserId = user.Id, Name = "Stopped", Strategy = "S", IsEnabled = true, IsRunning = false });
        context.BrokerConnections.Add(new BrokerConnection
        {
            Id = 40,
            UserId = user.Id,
            BrokerName = "Binance",
            ApiKey = "encrypted-key",
            ApiSecret = "encrypted-secret",
            IsActive = true,
            IsConnected = true,
            IsTestnet = true
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, 99, "Admin");
        var result = await controller.GetUsers();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var users = ok.Value.Should().BeAssignableTo<IEnumerable<object>>().Subject.ToList();
        users.Should().ContainSingle();

        var json = System.Text.Json.JsonSerializer.Serialize(users[0]);
        json.Should().NotContain("encrypted-key");
        json.Should().NotContain("encrypted-secret");
        json.Should().Contain("RunningBotCount");
        json.Should().Contain("ConnectedBrokerCount");
    }

    [Fact]
    public async Task DeactivateUser_DisablesAutoTrade_StopsRunningBots_AndAuditsAction()
    {
        await using var context = CreateContext();
        var user = new User
        {
            Id = 10,
            Email = "user@example.com",
            FirstName = "Test",
            LastName = "User",
            Role = "User",
            IsActive = true,
            IsAutoTradeEnabled = true
        };
        context.Users.Add(user);
        context.TradingBots.AddRange(
            new TradingBot { Id = 30, UserId = user.Id, Name = "Running", Strategy = "S", IsEnabled = true, IsRunning = true },
            new TradingBot { Id = 31, UserId = user.Id, Name = "Stopped", Strategy = "S", IsEnabled = true, IsRunning = false });
        await context.SaveChangesAsync();

        var controller = CreateController(context, 99, "Admin");
        var result = await controller.DeactivateUser(user.Id);

        result.Should().BeOfType<OkObjectResult>();
        var savedUser = await context.Users.SingleAsync(u => u.Id == user.Id);
        savedUser.IsActive.Should().BeFalse();
        savedUser.IsAutoTradeEnabled.Should().BeFalse();

        var bots = await context.TradingBots.Where(b => b.UserId == user.Id).ToListAsync();
        bots.Single(b => b.Id == 30).IsRunning.Should().BeFalse();
        bots.Single(b => b.Id == 31).IsRunning.Should().BeFalse();

        var audit = await context.AuditLogs.SingleAsync(a => a.Action == "AdminUserDeactivated");
        audit.UserId.Should().Be(user.Id);
        audit.Details.Should().Contain("stopped");
    }

    [Fact]
    public async Task DeactivateUser_CannotDeactivateCurrentAdmin()
    {
        await using var context = CreateContext();
        context.Users.Add(new User
        {
            Id = 99,
            Email = "admin@example.com",
            FirstName = "Admin",
            LastName = "User",
            Role = "Admin",
            IsActive = true
        });
        await context.SaveChangesAsync();

        var controller = CreateController(context, 99, "Admin");
        var result = await controller.DeactivateUser(99);

        result.Should().BeOfType<BadRequestObjectResult>();
        (await context.Users.SingleAsync(u => u.Id == 99)).IsActive.Should().BeTrue();
        (await context.AuditLogs.CountAsync()).Should().Be(0);
    }

    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ApplicationDbContext(options);
    }

    private static AdminController CreateController(ApplicationDbContext context, int userId, string role)
    {
        var controller = new AdminController(context)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                        new Claim(ClaimTypes.Role, role)
                    }, "TestAuth"))
                }
            }
        };

        return controller;
    }
}
