using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradingBotEngine.API.DTOs.Bot;
using TradingBotEngine.Data;
using TradingBotEngine.Data.Models;

namespace TradingBotEngine.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class BotController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public BotController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetBots()
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Invalid user identity." });

        var bots = await _context.TradingBots
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .Include(b => b.BrokerConnection)
            .Include(b => b.TrackedSymbols)
                .ThenInclude(x => x.TrackedSymbol)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync();

        return Ok(bots.Select(ToResponse).ToList());
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetBot(int id)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Invalid user identity." });

        var bot = await _context.TradingBots
            .AsNoTracking()
            .Where(b => b.Id == id && b.UserId == userId)
            .Include(b => b.BrokerConnection)
            .Include(b => b.TrackedSymbols)
                .ThenInclude(x => x.TrackedSymbol)
            .FirstOrDefaultAsync();

        return bot == null
            ? NotFound(new { message = "Trading bot not found." })
            : Ok(ToResponse(bot));
    }

    [HttpPost]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> CreateBot([FromBody] CreateTradingBotRequestDto request)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Invalid user identity." });

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var user = await _context.Users
            .Include(u => u.Subscription)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return NotFound(new { message = "User not found." });

        if (!user.IsActive)
            return BadRequest(new { message = "Account is deactivated." });

        if (!SubscriptionGuard.HasActiveSubscription(user))
            return BadRequest(new { message = "An active subscription is required to create a trading bot." });

        var name = request.Name.Trim();
        var strategy = request.Strategy.Trim();
        var timeframe = request.Timeframe.Trim().ToLowerInvariant();
        var symbolIds = request.TrackedSymbolIds.Distinct().ToList();

        if (await _context.TradingBots.AnyAsync(b => b.UserId == userId && b.Name == name))
            return Conflict(new { message = "A trading bot with this name already exists." });

        var symbols = await _context.TrackedSymbols
            .Where(s => s.UserId == userId && symbolIds.Contains(s.Id))
            .ToListAsync();

        if (symbols.Count != symbolIds.Count)
            return BadRequest(new { message = "One or more tracked symbols do not belong to this user." });

        if (symbols.Any(s => !s.IsEnabled))
            return BadRequest(new { message = "All assigned tracked symbols must be enabled." });

        var broker = await ValidateBrokerAsync(userId, request.BrokerConnectionId, symbols, request.UseFutures);
        if (broker.Error != null)
            return broker.Error;

        var riskError = ValidateRiskManagementFields(
            request.StopLossMode, request.StopLossPercent,
            request.TakeProfitMode, request.TakeProfitPercent);
        if (riskError != null)
            return BadRequest(new { message = riskError });

        var bot = new TradingBot
        {
            UserId = userId,
            Name = name,
            Strategy = strategy,
            Timeframe = timeframe,
            BrokerConnectionId = request.BrokerConnectionId,
            UseFutures = request.UseFutures,
            IsEnabled = true,
            IsRunning = false,
            CreatedAt = DateTime.UtcNow,
            AutoTradeEnabled = request.AutoTradeEnabled,
            StopLossMode = request.StopLossMode,
            StopLossPercent = request.StopLossMode == RiskManagementMode.Manual ? request.StopLossPercent : null,
            TakeProfitMode = request.TakeProfitMode,
            TakeProfitPercent = request.TakeProfitMode == RiskManagementMode.Manual ? request.TakeProfitPercent : null
        };

        foreach (var symbol in symbols)
        {
            bot.TrackedSymbols.Add(new TradingBotTrackedSymbol
            {
                TrackedSymbolId = symbol.Id
            });
        }

        _context.TradingBots.Add(bot);
        await AddAuditAsync(userId, "TradingBotCreated", $"Trading bot '{name}' was created.");
        await _context.SaveChangesAsync();

        await _context.Entry(bot).Reference(b => b.BrokerConnection).LoadAsync();
        await _context.Entry(bot).Collection(b => b.TrackedSymbols).Query().Include(x => x.TrackedSymbol).LoadAsync();

        return CreatedAtAction(nameof(GetBot), new { id = bot.Id }, ToResponse(bot));
    }

    [HttpPut("{id:int}")]
    [RequestSizeLimit(16 * 1024)]
    public async Task<IActionResult> UpdateBot(int id, [FromBody] UpdateTradingBotRequestDto request)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Invalid user identity." });

        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var bot = await _context.TradingBots
            .Include(b => b.BrokerConnection)
            .Include(b => b.TrackedSymbols)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (bot == null)
            return NotFound(new { message = "Trading bot not found." });

        if (bot.IsRunning)
            return Conflict(new { message = "Stop the trading bot before changing its configuration." });

        var name = request.Name.Trim();
        var strategy = request.Strategy.Trim();
        var timeframe = request.Timeframe.Trim().ToLowerInvariant();
        var symbolIds = request.TrackedSymbolIds.Distinct().ToList();

        if (await _context.TradingBots.AnyAsync(b => b.UserId == userId && b.Id != id && b.Name == name))
            return Conflict(new { message = "A trading bot with this name already exists." });

        var symbols = await _context.TrackedSymbols
            .Where(s => s.UserId == userId && symbolIds.Contains(s.Id))
            .ToListAsync();

        if (symbols.Count != symbolIds.Count)
            return BadRequest(new { message = "One or more tracked symbols do not belong to this user." });

        if (symbols.Any(s => !s.IsEnabled))
            return BadRequest(new { message = "All assigned tracked symbols must be enabled." });

        var broker = await ValidateBrokerAsync(userId, request.BrokerConnectionId, symbols, request.UseFutures);
        if (broker.Error != null)
            return broker.Error;

        var riskError = ValidateRiskManagementFields(
            request.StopLossMode, request.StopLossPercent,
            request.TakeProfitMode, request.TakeProfitPercent);
        if (riskError != null)
            return BadRequest(new { message = riskError });

        bot.Name = name;
        bot.Strategy = strategy;
        bot.Timeframe = timeframe;
        bot.BrokerConnectionId = request.BrokerConnectionId;
        bot.UseFutures = request.UseFutures;
        bot.UpdatedAt = DateTime.UtcNow;
        bot.AutoTradeEnabled = request.AutoTradeEnabled;
        bot.StopLossMode = request.StopLossMode;
        bot.StopLossPercent = request.StopLossMode == RiskManagementMode.Manual ? request.StopLossPercent : null;
        bot.TakeProfitMode = request.TakeProfitMode;
        bot.TakeProfitPercent = request.TakeProfitMode == RiskManagementMode.Manual ? request.TakeProfitPercent : null;

        _context.TradingBotTrackedSymbols.RemoveRange(bot.TrackedSymbols);
        bot.TrackedSymbols = symbolIds.Select(symbolId => new TradingBotTrackedSymbol
        {
            TradingBotId = bot.Id,
            TrackedSymbolId = symbolId
        }).ToList();

        await AddAuditAsync(userId, "TradingBotUpdated", $"Trading bot '{bot.Name}' (ID {bot.Id}) was updated.");
        await _context.SaveChangesAsync();

        await _context.Entry(bot).Reference(b => b.BrokerConnection).LoadAsync();
        await _context.Entry(bot).Collection(b => b.TrackedSymbols).Query().Include(x => x.TrackedSymbol).LoadAsync();

        return Ok(ToResponse(bot));
    }

    [HttpPost("{id:int}/start")]
    public async Task<IActionResult> StartBot(int id)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Invalid user identity." });

        var bot = await _context.TradingBots
            .Include(b => b.BrokerConnection)
            .Include(b => b.TrackedSymbols)
                .ThenInclude(x => x.TrackedSymbol)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (bot == null)
            return NotFound(new { message = "Trading bot not found." });

        if (!bot.IsEnabled)
            return BadRequest(new { message = "The trading bot is disabled." });

        if (bot.IsRunning)
            return Conflict(new { message = "Trading bot is already running." });

        var user = await _context.Users
            .Include(u => u.Subscription)
            .FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
            return NotFound(new { message = "User not found." });

       if (!SubscriptionGuard.HasActiveSubscription(user))
            return BadRequest(new { message = "An active subscription is required to start a trading bot." });

        var enabledSymbols = bot.TrackedSymbols
            .Where(x => x.TrackedSymbol.IsEnabled)
            .Select(x => x.TrackedSymbol)
            .ToList();

        if (enabledSymbols.Count == 0)
            return BadRequest(new { message = "The trading bot has no enabled tracked symbols." });

        if (bot.BrokerConnection == null)
            return BadRequest(new { message = "A broker connection must be selected before starting the bot." });

        if (!bot.BrokerConnection.IsActive || !bot.BrokerConnection.IsConnected)
            return Conflict(new { message = "The selected broker connection is not active and connected." });

        if (!bot.BrokerConnection.BrokerName.Equals("Binance", StringComparison.OrdinalIgnoreCase) &&
            !bot.BrokerConnection.BrokerName.Equals("Bybit", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "The selected broker is not supported." });

        if (enabledSymbols.Any(s => !s.Exchange.Equals(bot.BrokerConnection.BrokerName, StringComparison.OrdinalIgnoreCase)))
            return BadRequest(new { message = "All bot symbols must belong to the selected broker exchange." });

        bot.IsRunning = true;
        bot.UpdatedAt = DateTime.UtcNow;
        bot.LastStartedAt = DateTime.UtcNow;

        await AddAuditAsync(userId, "TradingBotStarted", $"Trading bot '{bot.Name}' (ID {bot.Id}) was started.");
        await _context.SaveChangesAsync();

        return Ok(ToResponse(bot));
    }

    [HttpPost("{id:int}/stop")]
    public async Task<IActionResult> StopBot(int id)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Invalid user identity." });

        var bot = await _context.TradingBots
            .Include(b => b.BrokerConnection)
            .Include(b => b.TrackedSymbols)
                .ThenInclude(x => x.TrackedSymbol)
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (bot == null)
            return NotFound(new { message = "Trading bot not found." });

        if (!bot.IsRunning)
            return Conflict(new { message = "Trading bot is already stopped." });

        bot.IsRunning = false;
        bot.UpdatedAt = DateTime.UtcNow;
        bot.LastStoppedAt = DateTime.UtcNow;

        await AddAuditAsync(userId, "TradingBotStopped", $"Trading bot '{bot.Name}' (ID {bot.Id}) was stopped.");
        await _context.SaveChangesAsync();

        return Ok(ToResponse(bot));
    }

    [HttpPost("{id:int}/enable")]
    public Task<IActionResult> EnableBot(int id) => SetEnabledAsync(id, true);

    [HttpPost("{id:int}/disable")]
    public Task<IActionResult> DisableBot(int id) => SetEnabledAsync(id, false);

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteBot(int id)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Invalid user identity." });

        var bot = await _context.TradingBots
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (bot == null)
            return NotFound(new { message = "Trading bot not found." });

        if (bot.IsRunning)
            return Conflict(new { message = "Stop the trading bot before deleting it." });

        _context.TradingBots.Remove(bot);
        await AddAuditAsync(userId, "TradingBotDeleted", $"Trading bot '{bot.Name}' (ID {bot.Id}) was deleted.");
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private async Task<IActionResult> SetEnabledAsync(int id, bool enabled)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Invalid user identity." });

        var bot = await _context.TradingBots
            .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);

        if (bot == null)
            return NotFound(new { message = "Trading bot not found." });

        if (!enabled && bot.IsRunning)
            return Conflict(new { message = "Stop the trading bot before disabling it." });

        if (bot.IsEnabled == enabled)
            return Ok(new { message = enabled ? "Trading bot is already enabled." : "Trading bot is already disabled." });

        bot.IsEnabled = enabled;
        bot.UpdatedAt = DateTime.UtcNow;

        await AddAuditAsync(userId, enabled ? "TradingBotEnabled" : "TradingBotDisabled", $"Trading bot ID {bot.Id} was {(enabled ? "enabled" : "disabled")}.");
        await _context.SaveChangesAsync();

        return Ok(new { success = true, isEnabled = bot.IsEnabled });
    }

    private async Task<(BrokerConnection? Broker, IActionResult? Error)> ValidateBrokerAsync(
        int userId,
        int? brokerConnectionId,
        List<TrackedSymbol> symbols,
        bool useFutures)
    {
        if (brokerConnectionId == null)
            return (null, null);

        var broker = await _context.BrokerConnections
            .FirstOrDefaultAsync(b => b.Id == brokerConnectionId.Value && b.UserId == userId);

        if (broker == null)
            return (null, BadRequest(new { message = "Selected broker connection does not belong to this user." }));

        if (!broker.IsActive)
            return (null, BadRequest(new { message = "Selected broker connection is inactive." }));

        if (useFutures && broker.BrokerName.Equals("Bybit", StringComparison.OrdinalIgnoreCase) == false &&
            broker.BrokerName.Equals("Binance", StringComparison.OrdinalIgnoreCase) == false)
            return (null, BadRequest(new { message = "Selected broker does not support futures in this application." }));

        if (symbols.Any(s => !s.Exchange.Equals(broker.BrokerName, StringComparison.OrdinalIgnoreCase)))
            return (null, BadRequest(new { message = "All assigned tracked symbols must belong to the selected broker exchange." }));

        return (broker, null);
    }

    private static bool HasActiveSubscription(User user) =>
        user.Subscription != null &&
        user.Subscription.IsActive &&
        user.Subscription.EndDate > DateTime.UtcNow;

    private static string? ValidateRiskManagementFields(
        RiskManagementMode stopLossMode, decimal? stopLossPercent,
        RiskManagementMode takeProfitMode, decimal? takeProfitPercent)
    {
        if (stopLossMode == RiskManagementMode.Manual && stopLossPercent is null)
            return "Stop-loss percent is required when stop-loss mode is Manual.";

        if (takeProfitMode == RiskManagementMode.Manual && takeProfitPercent is null)
            return "Take-profit percent is required when take-profit mode is Manual.";

        return null;
    }

    private async Task AddAuditAsync(int userId, string action, string details)
    {
        _context.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            Details = details,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers.UserAgent.ToString(),
            CreatedAt = DateTime.UtcNow
        });

        await Task.CompletedTask;
    }

    private static TradingBotResponseDto ToResponse(TradingBot bot) => new()
    {
        Id = bot.Id,
        Name = bot.Name,
        Strategy = bot.Strategy,
        Timeframe = bot.Timeframe,
        BrokerConnectionId = bot.BrokerConnectionId,
        BrokerName = bot.BrokerConnection?.BrokerName,
        UseFutures = bot.UseFutures,
        IsEnabled = bot.IsEnabled,
        IsRunning = bot.IsRunning,
        CreatedAt = bot.CreatedAt,
        UpdatedAt = bot.UpdatedAt,
        LastStartedAt = bot.LastStartedAt,
        LastStoppedAt = bot.LastStoppedAt,
        TrackedSymbols = bot.TrackedSymbols
            .Select(x => new TrackedBotSymbolResponseDto
            {
                Id = x.TrackedSymbol.Id,
                Symbol = x.TrackedSymbol.Symbol,
                Exchange = x.TrackedSymbol.Exchange,
                IsEnabled = x.TrackedSymbol.IsEnabled
            })
            .OrderBy(x => x.Symbol)
            .ToList(),
        AutoTradeEnabled = bot.AutoTradeEnabled,
        StopLossMode = bot.StopLossMode,
        StopLossPercent = bot.StopLossPercent,
        TakeProfitMode = bot.TakeProfitMode,
        TakeProfitPercent = bot.TakeProfitPercent
    };

    private bool TryGetUserId(out int userId)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(value, out userId);
    }
}
