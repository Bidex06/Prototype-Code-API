using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TradingBotEngine.API.DTOs.Trading;
using TradingBotEngine.Services;

namespace TradingBotEngine.API.Controllers;

[ApiController]
[Authorize]
[Route("api/Trading")]
public class TradingMarketDataController : ControllerBase
{
    private readonly BrokerService _brokerService;
    private readonly ExchangeMarketDataService _marketDataService;

    public TradingMarketDataController(
        BrokerService brokerService,
        ExchangeMarketDataService marketDataService)
    {
        _brokerService = brokerService;
        _marketDataService = marketDataService;
    }

    [HttpGet("market-price")]
    public async Task<IActionResult> GetMarketPrice(
        [FromQuery] int connectionId,
        [FromQuery] string symbol = "BTCUSDT")
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        if (connectionId <= 0)
            return BadRequest(new { message = "A valid connectionId is required." });

        var connection = await _brokerService.GetConnectionStatusAsync(userId, connectionId);
        if (connection == null || !connection.IsConnected)
            return Conflict(new { message = "The selected broker connection is not connected." });

        var snapshot = await _brokerService.GetSpotMarketPriceAsync(userId, symbol, connectionId);

        if (snapshot == null)
        {
            return BadRequest(new
            {
                success = false,
                message = "The selected broker did not return a valid market price."
            });
        }

        return Ok(new
        {
            success = true,
            connectionId = connection.Id,
            broker = snapshot.BrokerName,
            isTestnet = snapshot.IsTestnet,
            symbol = snapshot.Symbol,
            lastPrice = snapshot.LastPrice,
            suggestedBuyPrice = snapshot.SuggestedBuyPrice
        });
    }

    [HttpGet("symbols")]
    public async Task<IActionResult> GetSymbols(
        [FromQuery] int connectionId,
        [FromQuery] string market = "spot",
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized();

        if (connectionId <= 0)
            return BadRequest(new { message = "A valid connectionId is required." });

        var normalizedMarket = market.Trim().ToLowerInvariant();
        if (normalizedMarket is not ("spot" or "futures"))
            return BadRequest(new { message = "Market must be 'spot' or 'futures'." });

        var connection = await _brokerService.GetConnectionStatusAsync(userId, connectionId);
        if (connection == null)
            return NotFound(new { message = "Broker connection not found." });

        if (!connection.IsConnected)
            return Conflict(new { message = "The selected broker connection is not connected." });

        var symbols = await _marketDataService.GetTradableSymbolsAsync(
            connection.BrokerName,
            connection.IsTestnet,
            normalizedMarket == "futures",
            search,
            cancellationToken);

        var response = symbols.Select(symbol => new MarketSymbolResponseDto
        {
            Symbol = symbol.Symbol,
            BaseAsset = symbol.BaseAsset,
            QuoteAsset = symbol.QuoteAsset,
            Exchange = symbol.Exchange,
            Market = symbol.Market,
            Status = symbol.Status
        }).ToList();

        return Ok(response);
    }

    private bool TryGetUserId(out int userId)
    {
        return int.TryParse(
            User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"),
            out userId);
    }
}
