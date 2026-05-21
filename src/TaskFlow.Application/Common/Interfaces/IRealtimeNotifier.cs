namespace TaskFlow.Application.Common.Interfaces;

// Abstraction over realtime push (SignalR). Implemented in the API layer.
public interface IRealtimeNotifier
{
    Task NotifyUserAsync(long userId, string @event, object payload, CancellationToken ct = default);
    Task NotifyProjectAsync(long projectId, string @event, object payload, CancellationToken ct = default);
}

// No-op fallback so the domain works even when realtime is disabled.
public sealed class NullRealtimeNotifier : IRealtimeNotifier
{
    public Task NotifyUserAsync(long userId, string @event, object payload, CancellationToken ct = default) => Task.CompletedTask;
    public Task NotifyProjectAsync(long projectId, string @event, object payload, CancellationToken ct = default) => Task.CompletedTask;
}
