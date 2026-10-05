using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TradingBotEngine.API.DTOs.Trading;
using TradingBotEngine.Data;
using TradingBotEngine.Services;

namespace TradingBotEngine.API.Controllers;

/// <summary>
/// GET /api/trading/symbols - fills the symbol picker in the manual trade panel and Create Bot.
/// Kept in its own controller (same route prefix as TradingController) so it does not
/// touch the existing trading endpoints.
/// </summary>
[ApiController]
[Route("api/trading")]
[Authorize]
public sealed class MarketSymbolsController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ExchangeMarketDataService _marketData;

    public MarketSymbolsController(
        ApplicationDbContext context,
        ExchangeMarketDataService marketData)
    {
        _context = context;
        _marketData = marketData;
    }

    [HttpGet("symbols")]
    public async Task<IActionResult> GetSymbols(
        [FromQuery] int connectionId,
        [FromQuery] string market = "spot",
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized(new { message = "Invalid user identity." });

        // Only the caller's own connections may be used.
        var connection = await _context.BrokerConnections
            .AsNoTracking()
            .FirstOrDefaultAsync(
                b => b.Id == connectionId && b.UserId == userId,
                cancellationToken);

        if (connection == null)
            return NotFound(new { message = "Broker connection not found." });

        var useFutures = string.Equals(market, "futures", StringComparison.OrdinalIgnoreCase);

        try
        {
            var symbols = await _marketData.GetTradableSymbolsAsync(
                connection.BrokerName,
                connection.IsTestnet,
                useFutures,
                search,
                cancellationToken);

            var result = symbols
                .Take(300)
                .Select(x => new MarketSymbolResponseDto
                {
                    Symbol = x.Symbol,
                    BaseAsset = x.BaseAsset,
                    QuoteAsset = x.QuoteAsset,
                    Exchange = x.Exchange,
                    Market = x.Market,
                    Status = x.Status
                })
                .ToList();

            return Ok(result);
        }
        catch (NotSupportedException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[Symbols] Failed to load {connection.BrokerName} symbols " +
                $"(testnet={connection.IsTestnet}, futures={useFutures}): {ex.Message}");

            return StatusCode(502, new { message = "Unable to load trading pairs from the exchange right now." });
        }
    }
}
