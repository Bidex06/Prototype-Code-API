using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingBotEngine.Data;

namespace TradingBotEngine.API.Controllers
{
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

        // GET: /api/admin/stats
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var totalUsers = await _context.Users.CountAsync();

            var activeUsers = await _context.Users
                .CountAsync(u => u.IsActive);

            var activeSubscriptions = await _context.Subscriptions
                .CountAsync(s =>
                    s.IsActive &&
                    s.EndDate > DateTime.UtcNow);

            var trials = await _context.Subscriptions
                .CountAsync(s =>
                    s.IsTrial &&
                    s.IsActive &&
                    s.EndDate > DateTime.UtcNow);

            return Ok(new
            {
                totalUsers,
                activeUsers,
                activeSubscriptions,
                activeTrials = trials
            });
        }

        // GET: /api/admin/users
        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            var users = await _context.Users
                .Include(u => u.Subscription)
                .AsNoTracking()
                .OrderByDescending(u => u.Id)
                .Select(u => new
                {
                    id = u.Id,
                    email = u.Email,
                    firstName = u.FirstName,
                    lastName = u.LastName,
                    role = u.Role,
                    isActive = u.IsActive,
                    isEmailVerified = u.IsEmailVerified,
                    isAutoTradeEnabled = u.IsAutoTradeEnabled,
                    createdAt = u.CreatedAt,
                    subscriptionEnd = u.Subscription != null
                        ? u.Subscription.EndDate
                        : (DateTime?)null,
                    isTrial = u.Subscription != null &&
                              u.Subscription.IsTrial,
                    isSubscriptionActive = u.Subscription != null &&
                        u.Subscription.IsActive &&
                        u.Subscription.EndDate > DateTime.UtcNow
                })
                .ToListAsync();

            return Ok(users);
        }

        // GET: /api/admin/users/{id}
        [HttpGet("users/{id:int}")]
        public async Task<IActionResult> GetUser(int id)
        {
            var user = await _context.Users
                .Include(u => u.Subscription)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound(new
                {
                    message = "User not found."
                });

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
                lastLoginAt = user.LastLoginAt,
                subscription = user.Subscription == null
                    ? null
                    : new
                    {
                        plan = user.Subscription.Plan,
                        startDate = user.Subscription.StartDate,
                        endDate = user.Subscription.EndDate,
                        isActive = user.Subscription.IsActive,
                        isTrial = user.Subscription.IsTrial,
                        trialEndDate = user.Subscription.TrialEndDate
                    }
            });
        }

        // PUT: /api/admin/users/{id}/activate
        [HttpPut("users/{id:int}/activate")]
        public async Task<IActionResult> ActivateUser(int id)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound(new
                {
                    message = "User not found."
                });

            user.IsActive = true;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                userId = user.Id,
                isActive = user.IsActive,
                message = "User activated successfully."
            });
        }

        // PUT: /api/admin/users/{id}/deactivate
        [HttpPut("users/{id:int}/deactivate")]
        public async Task<IActionResult> DeactivateUser(int id)
        {
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Id == id);

            if (user == null)
                return NotFound(new
                {
                    message = "User not found."
                });

            user.IsActive = false;

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                userId = user.Id,
                isActive = user.IsActive,
                message = "User deactivated successfully."
            });
        }
    }
}