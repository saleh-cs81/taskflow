using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;
using TaskFlow.Infrastructure.Persistence;

namespace TaskFlow.Infrastructure.Services;

// Notifies assignees of tasks due within the next 24 hours (once per task).
public class DeadlineReminderWorker(IServiceScopeFactory scopeFactory, ILogger<DeadlineReminderWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunOnce(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Deadline reminder tick failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnce(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.UtcNow;
        var soon = now.AddHours(24);

        // Cross-tenant: bypass the tenant filter; stamp TenantId explicitly on the notification.
        var due = await db.Tasks.IgnoreQueryFilters()
            .Where(t => !t.IsDeleted && t.Status != WorkStatus.Done && t.AssigneeId != null
                        && t.ReminderSentUtc == null && t.DueDate != null
                        && t.DueDate <= soon && t.DueDate >= now)
            .ToListAsync(ct);

        foreach (var t in due)
        {
            db.Notifications.Add(new Notification
            {
                TenantId = t.TenantId,
                UserId = t.AssigneeId!.Value,
                Type = NotificationType.TaskDueSoon,
                Title = "Task due soon",
                Body = t.Title,
                LinkUrl = $"/tasks/{t.Id}"
            });
            t.ReminderSentUtc = now;
        }

        if (due.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Sent {Count} deadline reminder(s)", due.Count);
        }
    }
}
