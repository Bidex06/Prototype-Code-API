using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using TradingBotEngine.Data.Models;
using TradingBotEngine.Services;

namespace TradingBotEngine.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BrokerController : ControllerBase
    {
        private readonly BrokerService _brokerService;

        public BrokerController(BrokerService brokerService)
        {
            _brokerService = brokerService;
        }

        [HttpGet("connections")]
        public async Task<IActionResult> GetConnections()
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user identity." });

            return Ok(await _brokerService.GetConnectionsAsync(userId));
        }

        [HttpPost("connect")]
        [RequestSizeLimit(16 * 1024)]
        public async Task<IActionResult> Connect([FromBody] ConnectBrokerRequest request)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user identity." });

            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            var connection = new BrokerConnection
            {
                UserId = userId,
                BrokerName = request.Broker.Trim(),
                ApiKey = request.ApiKey.Trim(),
                ApiSecret = request.ApiSecret.Trim(),
                IsActive = true,
                IsConnected = false,
                IsTestnet = true,
                IsLiveTradingEnabled = false,
                CreatedAt = DateTime.UtcNow
            };

            var connected = await _brokerService.ConnectAsync(userId, connection);
            if (!connected)
                return BadRequest(new { success = false, message = "Broker connection failed. Verify the credentials and testnet account." });

            return Ok(new
            {
                success = true,
                message = "Broker connected in TESTNET mode.",
                isTestnet = true
            });
        }

        [HttpPost("disconnect")]
        public async Task<IActionResult> Disconnect([FromBody] DisconnectBrokerRequest? request)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user identity." });

            var success = await _brokerService.DisconnectAsync(userId, request?.Broker);
            return success
                ? Ok(new { success = true, message = "Broker disconnected." })
                : NotFound(new { success = false, message = "No active broker connection found." });
        }

        [HttpPost("{connectionId:int}/live-trading") ]
        public async Task<IActionResult> SetLiveTrading(int connectionId, [FromBody] LiveTradingRequest request)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "Invalid user identity." });

            if (!request.Confirm)
                return BadRequest(new { message = "Explicit confirmation is required before enabling live trading." });

            var success = await _brokerService.SetLiveTradingOptInAsync(userId, connectionId, request.Enabled);
            if (!success)
                return NotFound(new { message = "Broker connection not found." });

            return Ok(new
            {
                success = true,
                isLiveTradingEnabled = request.Enabled,
                isTestnet = !request.Enabled
            });
        }

        private bool TryGetUserId(out int userId) =>
            int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }

    public sealed class ConnectBrokerRequest
    {
        [Required, RegularExpression("^(binance|bybit)$", ErrorMessage = "Broker must be Binance or Bybit.")]
        public string Broker { get; set; } = string.Empty;

        [Required, StringLength(256, MinimumLength = 8)]
        public string ApiKey { get; set; } = string.Empty;

        [Required, StringLength(256, MinimumLength = 8)]
        public string ApiSecret { get; set; } = string.Empty;
    }

    public sealed class DisconnectBrokerRequest
    {
        [StringLength(50)]
        public string? Broker { get; set; }
    }

    public sealed class LiveTradingRequest
    {
        public bool Enabled { get; set; }
        public bool Confirm { get; set; }
    }
}
