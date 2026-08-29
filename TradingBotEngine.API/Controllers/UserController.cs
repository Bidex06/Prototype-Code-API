using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradingBotEngine.Data;

namespace TradingBotEngine.API.Controllers
{
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
                return Unauthorized(new { message = "Invalid user identity." });

            var user = await _context.Users
                .Include(u => u.Subscription)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return NotFound(new { message = "User not found." });

            var subscriptionActive = user.Subscription != null &&
                                     user.Subscription.IsActive &&
                                     user.Subscription.EndDate > DateTime.UtcNow;

            return Ok(new
            {
                id = user.Id,
                email = user.Email,
                firstName = user.FirstName,
                lastName = user.LastName,
                role = user.Role,
                isActive = user.IsActive,
                isEmailVerified = user.IsEmailVerified,
                isAutoTradeEnabled = user.IsAutoTradeEnabled,
                subscriptionEnd = user.Subscription?.EndDate,
                isTrial = user.Subscription?.IsTrial ?? false,
                isSubscriptionActive = subscriptionActive
            });
        }

        [HttpPut("auto-trade")]
        public async Task<IActionResult> ToggleAutoTrade([FromBody] ToggleAutoTradeRequest request)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user identity." });

            var user = await _context.Users
                .Include(u => u.Subscription)
                .FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null)
                return NotFound(new { message = "User not found." });

            if (!user.IsActive)
                return BadRequest(new { message = "Account is deactivated." });

            if (request.IsAutoTradeEnabled &&
                (user.Subscription == null || !user.Subscription.IsActive || user.Subscription.EndDate <= DateTime.UtcNow))
                return BadRequest(new { message = "An active subscription is required before enabling auto-trade." });

            user.IsAutoTradeEnabled = request.IsAutoTradeEnabled;
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                isAutoTradeEnabled = user.IsAutoTradeEnabled,
                message = $"Auto-trade {(user.IsAutoTradeEnabled ? "enabled" : "disabled")}"
            });
        }

        [HttpGet("auto-trade-status")]
        public async Task<IActionResult> GetAutoTradeStatus()
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user identity." });

            var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null)
                return NotFound(new { message = "User not found." });

            return Ok(new { isAutoTradeEnabled = user.IsAutoTradeEnabled });
        }

        private bool TryGetUserId(out int userId)
        {
            return int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
        }
    }

    public sealed class ToggleAutoTradeRequest
    {
        public bool IsAutoTradeEnabled { get; set; }
    }
}
