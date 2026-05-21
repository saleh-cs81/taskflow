using Microsoft.AspNetCore.SignalR;
using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.API.Realtime;

// SignalR-backed implementation of the domain's realtime abstraction.
public class SignalRRealtimeNotifier(
    IHubContext<NotificationsHub> notifications,
    IHubContext<BoardHub> board) : IRealtimeNotifier
{
    public Task NotifyUserAsync(long userId, string @event, object payload, CancellationToken ct = default)
        => notifications.Clients.Group($"user:{userId}").SendAsync(@event, payload, ct);

    public Task NotifyProjectAsync(long projectId, string @event, object payload, CancellationToken ct = default)
        => board.Clients.Group($"project:{projectId}").SendAsync(@event, payload, ct);
}
