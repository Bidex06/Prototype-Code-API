using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using TradingBotEngine.Services;

namespace TradingBotEngine.API.Controllers;

[ApiController]
[Route("api/testnet-cleanup")]
[Authorize]
//[ApiExplorerSettings(IgnoreApi = true)]
public sealed class TestnetCleanupController : ControllerBase
{
    private readonly BrokerService _brokerService;

    public TestnetCleanupController(BrokerService brokerService)
    {
        _brokerService = brokerService;
    }

    [HttpGet("open-orders")]
    public async Task<IActionResult> GetOpenOrders(
        [FromQuery] string symbol = "BTCUSDT")
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Invalid user identity." });

        if (string.IsNullOrWhiteSpace(symbol))
            return BadRequest(new { message = "Symbol is required." });

        var orders = await _brokerService.GetOpenOrdersAsync(
            userId,
            symbol.Trim().ToUpperInvariant(),
            useFutures: false);

        return Ok(orders ?? Array.Empty<object>());
    }

    [HttpPost("cancel-open-order")]
    public async Task<IActionResult> CancelOpenOrder(
        [FromBody] CancelOpenOrderRequest request)
    {
        if (!TryGetUserId(out var userId))
            return Unauthorized(new { message = "Invalid user identity." });

        if (string.IsNullOrWhiteSpace(request.Symbol) ||
            string.IsNullOrWhiteSpace(request.OrderId))
            return BadRequest(new { message = "Symbol and OrderId are required." });

        var success = await _brokerService.CancelOpenExchangeOrderForUserAsync(
            userId,
            request.Symbol.Trim().ToUpperInvariant(),
            request.OrderId.Trim(),
            useFutures: false);

        if (!success)
        {
            return BadRequest(new
            {
                success = false,
                message = "The order was not cancelled. It must belong to your connected Binance Testnet account and currently be open."
            });
        }

        return Ok(new
        {
            success = true,
            message = "Open Binance Testnet order cancelled."
        });
    }

    private bool TryGetUserId(out int userId)
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return int.TryParse(claim, out userId) && userId > 0;
    }

    public sealed class CancelOpenOrderRequest
    {
        public string Symbol { get; set; } = "BTCUSDT";
        public string OrderId { get; set; } = string.Empty;
    }
}
