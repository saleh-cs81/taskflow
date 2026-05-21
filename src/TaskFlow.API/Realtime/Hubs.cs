using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace TaskFlow.API.Realtime;

// Per-user notifications. Each connection joins a group named "user:{userId}".
[Authorize]
public class NotificationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userId = Context.UserIdentifier;
        if (!string.IsNullOrEmpty(userId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId}");
        await base.OnConnectedAsync();
    }
}

// Project board updates. Clients call JoinProject/LeaveProject to subscribe.
[Authorize]
public class BoardHub : Hub
{
    public Task JoinProject(long projectId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, $"project:{projectId}");

    public Task LeaveProject(long projectId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project:{projectId}");
}
