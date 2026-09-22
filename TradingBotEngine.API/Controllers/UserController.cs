using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradingBotEngine.API.DTOs.User;
using TradingBotEngine.Data;
using TradingBotEngine.Data.Models;

namespace TradingBotEngine.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public UserController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                message = "Invalid user identity."
            });
        }

        var user = await _context.Users
            .Include(u => u.Subscription)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        var subscriptionActive =
            user.Subscription != null &&
            user.Subscription.IsActive &&
            user.Subscription.EndDate > DateTime.UtcNow;

        var response = new UserProfileResponseDto
        {
            Id = user.Id,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role,
            IsActive = user.IsActive,
            IsEmailVerified = user.IsEmailVerified,
            IsAutoTradeEnabled = user.IsAutoTradeEnabled,
            SubscriptionEnd = user.Subscription?.EndDate,
            IsTrial = user.Subscription?.IsTrial ?? false,
            IsSubscriptionActive = subscriptionActive
        };

        return Ok(response);
    }

    [HttpPut("auto-trade")]
    public async Task<IActionResult> ToggleAutoTrade(
        [FromBody] ToggleAutoTradeRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                message = "Invalid user identity."
            });
        }

        var user = await _context.Users
            .Include(u => u.Subscription)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        if (!user.IsActive)
        {
            return BadRequest(new
            {
                message = "Account is deactivated."
            });
        }

        if (request.IsAutoTradeEnabled &&
            (user.Subscription == null ||
             !user.Subscription.IsActive ||
             user.Subscription.EndDate <= DateTime.UtcNow))
        {
            return BadRequest(new
            {
                message =
                    "An active subscription is required before enabling auto-trade."
            });
        }

        user.IsAutoTradeEnabled =
            request.IsAutoTradeEnabled;

        await _context.SaveChangesAsync();

        return Ok(new AutoTradeResponseDto
        {
            Success = true,
            IsAutoTradeEnabled =
                user.IsAutoTradeEnabled,
            Message =
                $"Auto-trade {(user.IsAutoTradeEnabled ? "enabled" : "disabled")}"
        });
    }

    [HttpGet("auto-trade-status")]
    public async Task<IActionResult> GetAutoTradeStatus()
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                message = "Invalid user identity."
            });
        }

        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        return Ok(new AutoTradeStatusResponseDto
        {
            IsAutoTradeEnabled =
                user.IsAutoTradeEnabled
        });
    }

    [HttpPut("emergency-kill-switch")]
    public async Task<IActionResult> SetEmergencyKillSwitch(
        [FromBody] EmergencyKillSwitchRequestDto request)
    {
        if (!TryGetUserId(out var userId))
        {
            return Unauthorized(new
            {
                message = "Invalid user identity."
            });
        }

        var riskSetting = await _context.RiskSettings
            .FirstOrDefaultAsync(r => r.UserId == userId);

        if (riskSetting == null)
        {
            return NotFound(new
            {
                message =
                    "Risk settings are not configured for this user."
            });
        }

        var now = DateTime.UtcNow;

        riskSetting.IsEmergencyKillSwitch =
            request.Enabled;

        riskSetting.EmergencyKillSwitchActivatedAt =
            request.Enabled ? now : null;

        riskSetting.UpdatedAt = now;

        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = request.Enabled
                ? "EmergencyKillSwitchActivated"
                : "EmergencyKillSwitchDeactivated",
            Details = request.Enabled
                ? "Emergency kill switch activated. New trading orders are blocked."
                : "Emergency kill switch deactivated. New trading orders may proceed if all other risk controls pass.",
            CreatedAt = now
        });

        await _context.SaveChangesAsync();

        return Ok(new EmergencyKillSwitchResponseDto
        {
            IsEmergencyKillSwitch =
                riskSetting.IsEmergencyKillSwitch,
            EmergencyKillSwitchActivatedAt =
                riskSetting.EmergencyKillSwitchActivatedAt
        });
    }

    private bool TryGetUserId(out int userId)
    {
        return int.TryParse(
            User.FindFirstValue(
                ClaimTypes.NameIdentifier),
            out userId);
    }
}