using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace backend.Hubs
{
    [Authorize]
    public sealed class GameHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"user:{userId}");
            }
            await base.OnDisconnectedAsync(exception);
        }

        public async Task JoinGame(string gameId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, gameId);
            await Clients.Group(gameId).SendAsync("player:connected", new
            {
                connectionId = Context.ConnectionId,
                userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            });
        }

        public async Task LeaveGame(string gameId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, gameId);
            await Clients.Group(gameId).SendAsync("player:disconnected", new
            {
                connectionId = Context.ConnectionId,
                userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            });
        }

        public async Task SendGameMessage(string gameId, string message)
        {
            var userId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var displayName = Context.User?.FindFirst("displayName")?.Value ?? "Unknown";

            await Clients.Group(gameId).SendAsync("game:message", new
            {
                userId,
                displayName,
                message,
                timestamp = DateTime.UtcNow
            });
        }
    }
}