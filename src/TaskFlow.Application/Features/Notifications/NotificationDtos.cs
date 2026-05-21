using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Notifications;

public record NotificationDto(
    long Id,
    NotificationType Type,
    string Title,
    string? Body,
    string? LinkUrl,
    bool IsRead,
    DateTime CreatedAtUtc);

public record ActivityDto(
    long Id,
    long? UserId,
    string Action,
    string? EntityType,
    long? EntityId,
    DateTime CreatedAtUtc);

// Internal request used by other services to raise a notification.
public record CreateNotification(
    long UserId,
    NotificationType Type,
    string Title,
    string? Body = null,
    string? LinkUrl = null);

public interface INotificationService
{
    Task CreateAsync(CreateNotification notification, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationDto>> ListAsync(bool unreadOnly, CancellationToken ct = default);
    Task<int> UnreadCountAsync(CancellationToken ct = default);
    Task MarkReadAsync(long id, CancellationToken ct = default);
    Task MarkAllReadAsync(CancellationToken ct = default);
}

public interface IActivityService
{
    Task LogAsync(string action, string entityType, long entityId, CancellationToken ct = default);
    Task<IReadOnlyList<ActivityDto>> GetFeedAsync(int take, CancellationToken ct = default);
}
