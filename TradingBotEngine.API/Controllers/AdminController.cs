using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingBotEngine.API.DTOs.Admin;
using TradingBotEngine.Data;
using TradingBotEngine.Data.Models;

namespace TradingBotEngine.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public AdminController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("stats")]
    public async Task<IActionResult> GetStats()
    {
        var now = DateTime.UtcNow;

        var response = new AdminStatsResponseDto
        {
            TotalUsers = await _context.Users.CountAsync(),
            ActiveUsers = await _context.Users.CountAsync(u => u.IsActive),
            ActiveSubscriptions = await _context.Subscriptions.CountAsync(s => s.IsActive && s.EndDate > now),
            ActiveTrials = await _context.Subscriptions.CountAsync(s => s.IsTrial && s.IsActive && s.EndDate > now),
            TotalBots = await _context.TradingBots.CountAsync(),
            RunningBots = await _context.TradingBots.CountAsync(b => b.IsEnabled && b.IsRunning),
            ConnectedBrokers = await _context.BrokerConnections.CountAsync(b => b.IsActive && b.IsConnected)
        };

        return Ok(response);
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var now = DateTime.UtcNow;

        var users = await _context.Users
            .AsNoTracking()
            .OrderByDescending(u => u.Id)
            .Select(u => new AdminUserResponseDto
            {
                Id = u.Id,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                Role = u.Role,
                IsActive = u.IsActive,
                IsEmailVerified = u.IsEmailVerified,
                IsAutoTradeEnabled = u.IsAutoTradeEnabled,
                CreatedAt = u.CreatedAt,
                LastLoginAt = u.LastLoginAt,
                SubscriptionEnd = u.Subscription == null ? null : u.Subscription.EndDate,
                IsTrial = u.Subscription != null && u.Subscription.IsTrial,
                IsSubscriptionActive = u.Subscription != null && u.Subscription.IsActive && u.Subscription.EndDate > now,
                BotCount = u.TradingBots.Count(),
                RunningBotCount = u.TradingBots.Count(b => b.IsEnabled && b.IsRunning),
                BrokerConnectionCount = u.BrokerConnections.Count(b => b.IsActive),
                ConnectedBrokerCount = u.BrokerConnections.Count(b => b.IsActive && b.IsConnected)
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("users/{id:int}")]
    public async Task<IActionResult> GetUser(int id)
    {
        var user = await _context.Users
            .Include(u => u.Subscription)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
            return NotFound(new { message = "User not found." });

        return Ok(new AdminUserDetailResponseDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role,
            IsActive = user.IsActive,
            IsEmailVerified = user.IsEmailVerified,
            IsAutoTradeEnabled = user.IsAutoTradeEnabled,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            Subscription = user.Subscription == null ? null : ToSubscriptionDto(user.Subscription)
        });
    }

    [HttpGet("bots")]
    public async Task<IActionResult> GetBots([FromQuery] int? userId = null)
    {
        var query = _context.TradingBots
            .AsNoTracking()
            .Include(b => b.User)
            .Include(b => b.BrokerConnection)
            .AsQueryable();

        if (userId.HasValue)
            query = query.Where(b => b.UserId == userId.Value);

        var bots = await query
            .OrderByDescending(b => b.Id)
            .Select(b => new AdminBotResponseDto
            {
                Id = b.Id,
                UserId = b.UserId,
                UserEmail = b.User.Email,
                Name = b.Name,
                Strategy = b.Strategy,
                Timeframe = b.Timeframe,
                BrokerConnectionId = b.BrokerConnectionId,
                BrokerName = b.BrokerConnection == null ? null : b.BrokerConnection.BrokerName,
                IsTestnet = b.BrokerConnection == null || b.BrokerConnection.IsTestnet,
                IsLiveTradingEnabled = b.BrokerConnection != null && b.BrokerConnection.IsLiveTradingEnabled,
                UseFutures = b.UseFutures,
                IsEnabled = b.IsEnabled,
                IsRunning = b.IsRunning,
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt,
                LastStartedAt = b.LastStartedAt,
                LastStoppedAt = b.LastStoppedAt,
                TrackedSymbolCount = b.TrackedSymbols.Count()
            })
            .ToListAsync();

        return Ok(bots);
    }

    [HttpGet("broker-connections")]
    public async Task<IActionResult> GetBrokerConnections([FromQuery] int? userId = null)
    {
        var query = _context.BrokerConnections
            .AsNoTracking()
            .Include(b => b.User)
            .AsQueryable();

        if (userId.HasValue)
            query = query.Where(b => b.UserId == userId.Value);

        var connections = await query
            .OrderByDescending(b => b.Id)
            .Select(b => new AdminBrokerConnectionResponseDto
            {
                Id = b.Id,
                UserId = b.UserId,
                UserEmail = b.User.Email,
                BrokerName = b.BrokerName,
                AccountId = b.AccountId,
                IsActive = b.IsActive,
                IsConnected = b.IsConnected,
                IsTestnet = b.IsTestnet,
                IsLiveTradingEnabled = b.IsLiveTradingEnabled,
                LiveTradingOptInAt = b.LiveTradingOptInAt,
                LastConnectedAt = b.LastConnectedAt,
                LastDisconnectedAt = b.LastDisconnectedAt,
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt
            })
            .ToListAsync();

        return Ok(connections);
    }

    [HttpGet("subscriptions")]
    public async Task<IActionResult> GetSubscriptions([FromQuery] int? userId = null)
    {
        var query = _context.Subscriptions
            .AsNoTracking()
            .Include(s => s.User)
            .AsQueryable();

        if (userId.HasValue)
            query = query.Where(s => s.UserId == userId.Value);

        var subscriptions = await query
            .OrderByDescending(s => s.Id)
            .Select(s => new AdminSubscriptionResponseDto
            {
                Id = s.Id,
                UserId = s.UserId,
                Plan = s.Plan,
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                IsActive = s.IsActive,
                IsTrial = s.IsTrial,
                TrialEndDate = s.TrialEndDate,
                PaymentReference = s.PaymentReference
            })
            .ToListAsync();

        return Ok(subscriptions);
    }

    [HttpGet("audit-logs")]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] int? userId = null,
        [FromQuery] string? action = null,
        [FromQuery] int take = 100)
    {
        take = Math.Clamp(take, 1, 200);

        var query = _context.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .AsQueryable();

        if (userId.HasValue)
            query = query.Where(a => a.UserId == userId.Value);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action == action);

        var logs = await query
            .OrderByDescending(a => a.CreatedAt)
            .Take(take)
            .Select(a => new AdminAuditLogResponseDto
            {
                Id = a.Id,
                UserId = a.UserId,
                UserEmail = a.User == null ? null : a.User.Email,
                Action = a.Action,
                Details = a.Details,
                IpAddress = a.IpAddress,
                UserAgent = a.UserAgent,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return Ok(logs);
    }

    [HttpPut("users/{id:int}/activate")]
    public async Task<IActionResult> ActivateUser(int id)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null)
            return NotFound(new { message = "User not found." });

        if (user.IsActive)
        {
            return Ok(new AdminUserStatusResponseDto
            {
                Success = true,
                UserId = user.Id,
                IsActive = true,
                Message = "User is already active."
            });
        }

        user.IsActive = true;
        AddAdminAudit(user.Id, "AdminUserActivated", $"User {user.Id} was activated by admin {GetAdminUserId()}.");
        await _context.SaveChangesAsync();

        return Ok(new AdminUserStatusResponseDto
        {
            Success = true,
            UserId = user.Id,
            IsActive = true,
            Message = "User activated successfully."
        });
    }

    [HttpPut("users/{id:int}/deactivate")]
    public async Task<IActionResult> DeactivateUser(int id)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null)
            return NotFound(new { message = "User not found." });

        var adminUserId = GetAdminUserId();
        if (adminUserId.HasValue && adminUserId.Value == id)
        {
            return BadRequest(new { message = "An administrator cannot deactivate their own account." });
        }

        user.IsActive = false;
        user.IsAutoTradeEnabled = false;

        var runningBots = await _context.TradingBots
            .Where(b => b.UserId == id && b.IsRunning)
            .ToListAsync();

        foreach (var bot in runningBots)
        {
            bot.IsRunning = false;
            bot.LastStoppedAt = DateTime.UtcNow;
            bot.UpdatedAt = DateTime.UtcNow;
        }

        AddAdminAudit(
            user.Id,
            "AdminUserDeactivated",
            $"User {user.Id} was deactivated by admin {adminUserId?.ToString() ?? "unknown"}. " +
            $"Auto-trading was disabled and {runningBots.Count} running bot(s) were stopped.");

        await _context.SaveChangesAsync();

        return Ok(new AdminUserStatusResponseDto
        {
            Success = true,
            UserId = user.Id,
            IsActive = false,
            Message = "User deactivated successfully. Auto-trading was disabled and running bots were stopped."
        });
    }

    private void AddAdminAudit(int targetUserId, string action, string details)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = targetUserId,
            Action = action,
            Details = details,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            CreatedAt = DateTime.UtcNow
        });
    }

    private int? GetAdminUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out var id) ? id : null;
    }

    private static AdminSubscriptionResponseDto ToSubscriptionDto(Subscription subscription) => new()
    {
        Id = subscription.Id,
        UserId = subscription.UserId,
        Plan = subscription.Plan,
        StartDate = subscription.StartDate,
        EndDate = subscription.EndDate,
        IsActive = subscription.IsActive,
        IsTrial = subscription.IsTrial,
        TrialEndDate = subscription.TrialEndDate,
        PaymentReference = subscription.PaymentReference
    };
}
