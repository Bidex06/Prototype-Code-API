using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace TradingBotEngine.API.Hubs;

public sealed class TradingHubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        return connection.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}