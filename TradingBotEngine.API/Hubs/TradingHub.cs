using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace TradingBotEngine.API.Hubs
{
    [Authorize]
    public class TradingHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId))
                await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!string.IsNullOrWhiteSpace(userId))
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup(userId));

            await base.OnDisconnectedAsync(exception);
        }

        // Clients cannot broadcast balance or signal events. Server-side services
        // should publish to the authenticated user's group through IHubContext.
        private static string UserGroup(string userId) => $"user:{userId}";
    }
}
