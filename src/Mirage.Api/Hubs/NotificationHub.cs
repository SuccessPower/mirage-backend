using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Mirage.Api.Hubs;

[Authorize]
public sealed class NotificationHub : Hub
{
    // Each connection joins a group scoped to the authenticated user so the
    // server can push notifications with Clients.Group(UserGroup(userId)).
    public override async Task OnConnectedAsync()
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(GetUserId()));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, UserGroup(GetUserId()));
        await base.OnDisconnectedAsync(exception);
    }

    private Guid GetUserId() =>
        Guid.Parse(Context.User!.FindFirstValue("sub")
            ?? throw new InvalidOperationException("User ID claim is missing."));

    public static string UserGroup(Guid userId) => $"notifications:{userId}";
}
