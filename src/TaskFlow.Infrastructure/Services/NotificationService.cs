using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Notifications;
using TaskFlow.Domain.Entities;

namespace TaskFlow.Infrastructure.Services;

public class NotificationService(
    IAppDbContext db,
    ICurrentUser currentUser,
    IRealtimeNotifier realtime) : INotificationService
{
    public async Task CreateAsync(CreateNotification n, CancellationToken ct = default)
    {
        var notification = new Notification
        {
            UserId = n.UserId,
            Type = n.Type,
            Title = n.Title,
            Body = n.Body,
            LinkUrl = n.LinkUrl,
            IsRead = false
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync(ct);

        // Push live to the recipient (no-op if realtime disabled).
        await realtime.NotifyUserAsync(n.UserId, "notification", new NotificationDto(
            notification.Id, notification.Type, notification.Title, notification.Body,
            notification.LinkUrl, false, notification.CreatedAtUtc), ct);
    }

    public async Task<IReadOnlyList<NotificationDto>> ListAsync(bool unreadOnly, CancellationToken ct = default)
    {
        var uid = currentUser.UserId ?? throw new UnauthorizedAppException("error.unauthorized");
        var query = db.Notifications.AsNoTracking().Where(x => x.UserId == uid);
        if (unreadOnly) query = query.Where(x => !x.IsRead);
        return await query.OrderByDescending(x => x.CreatedAtUtc).Take(100)
            .Select(x => new NotificationDto(x.Id, x.Type, x.Title, x.Body, x.LinkUrl, x.IsRead, x.CreatedAtUtc))
            .ToListAsync(ct);
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct = default)
    {
        var uid = currentUser.UserId ?? throw new UnauthorizedAppException("error.unauthorized");
        return await db.Notifications.CountAsync(x => x.UserId == uid && !x.IsRead, ct);
    }

    public async Task MarkReadAsync(long id, CancellationToken ct = default)
    {
        var uid = currentUser.UserId ?? throw new UnauthorizedAppException("error.unauthorized");
        var n = await db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == uid, ct)
            ?? throw new NotFoundAppException("error.not_found");
        if (!n.IsRead) { n.IsRead = true; n.ReadAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(ct); }
    }

    public async Task MarkAllReadAsync(CancellationToken ct = default)
    {
        var uid = currentUser.UserId ?? throw new UnauthorizedAppException("error.unauthorized");
        var unread = await db.Notifications.Where(x => x.UserId == uid && !x.IsRead).ToListAsync(ct);
        foreach (var n in unread) { n.IsRead = true; n.ReadAtUtc = DateTime.UtcNow; }
        if (unread.Count > 0) await db.SaveChangesAsync(ct);
    }
}

public class ActivityService(IAppDbContext db, ICurrentUser currentUser) : IActivityService
{
    public async Task LogAsync(string action, string entityType, long entityId, CancellationToken ct = default)
    {
        db.ActivityLogs.Add(new ActivityLog
        {
            UserId = currentUser.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<ActivityDto>> GetFeedAsync(int take, CancellationToken ct = default)
        => await db.ActivityLogs.AsNoTracking()
            .OrderByDescending(a => a.CreatedAtUtc)
            .Take(Math.Clamp(take, 1, 200))
            .Select(a => new ActivityDto(a.Id, a.UserId, a.Action, a.EntityType, a.EntityId, a.CreatedAtUtc))
            .ToListAsync(ct);
}
