using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Reports;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Services;

public class ReportService(IAppDbContext db, IDateTime clock) : IReportService
{
    public async Task<DashboardStatsDto> GetDashboardAsync(CancellationToken ct = default)
    {
        var totalProjects = await db.Projects.CountAsync(ct);
        var activeProjects = await db.Projects.CountAsync(p => p.Status == ProjectStatus.Active, ct);

        var openTasks = await db.Tasks.CountAsync(t => t.Status != WorkStatus.Done, ct);
        var completedTasks = await db.Tasks.CountAsync(t => t.Status == WorkStatus.Done, ct);

        var now = clock.UtcNow;
        var overdueTasks = await db.Tasks.CountAsync(
            t => t.Status != WorkStatus.Done && t.DueDate != null && t.DueDate < now, ct);

        var weekStart = now.Date.AddDays(-(int)now.DayOfWeek);
        var weekEntries = await db.TimeEntries
            .Where(e => !e.IsRunning && e.StartUtc >= weekStart)
            .Select(e => new { e.DurationSeconds, e.IsBillable })
            .ToListAsync(ct);

        var hours = Math.Round(weekEntries.Sum(e => e.DurationSeconds) / 3600.0, 2);
        var billable = Math.Round(weekEntries.Where(e => e.IsBillable).Sum(e => e.DurationSeconds) / 3600.0, 2);

        return new DashboardStatsDto(totalProjects, activeProjects, openTasks, completedTasks, overdueTasks, hours, billable);
    }

    public async Task<IReadOnlyList<ProjectProgressDto>> GetProjectProgressAsync(CancellationToken ct = default)
    {
        var projects = await db.Projects.AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.Status })
            .ToListAsync(ct);

        var taskCounts = await db.Tasks.AsNoTracking()
            .Where(t => t.ParentTaskId == null)
            .GroupBy(t => new { t.ProjectId, t.Status })
            .Select(g => new { g.Key.ProjectId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        return projects.Select(p =>
        {
            var rows = taskCounts.Where(c => c.ProjectId == p.Id).ToList();
            var todo = rows.Where(r => r.Status == WorkStatus.Todo || r.Status == WorkStatus.Blocked).Sum(r => r.Count);
            var inProgress = rows.Where(r => r.Status == WorkStatus.InProgress || r.Status == WorkStatus.InReview).Sum(r => r.Count);
            var done = rows.Where(r => r.Status == WorkStatus.Done).Sum(r => r.Count);
            var total = todo + inProgress + done;
            var pct = total == 0 ? 0 : Math.Round(done * 100.0 / total, 1);
            return new ProjectProgressDto(p.Id, p.Name, p.Status, total, todo, inProgress, done, pct);
        }).OrderByDescending(p => p.TotalTasks).ToList();
    }

    public async Task<IReadOnlyList<ProductivityRowDto>> GetProductivityAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        var users = await db.Users.AsNoTracking().Select(u => new { u.Id, u.FullName }).ToListAsync(ct);

        var completed = await db.Tasks.AsNoTracking()
            .Where(t => t.Status == WorkStatus.Done && t.CompletedAtUtc != null
                        && t.CompletedAtUtc >= fromUtc && t.CompletedAtUtc < toUtc && t.AssigneeId != null)
            .GroupBy(t => t.AssigneeId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var time = await db.TimeEntries.AsNoTracking()
            .Where(e => !e.IsRunning && e.StartUtc >= fromUtc && e.StartUtc < toUtc)
            .GroupBy(e => e.UserId)
            .Select(g => new { UserId = g.Key, Seconds = g.Sum(x => x.DurationSeconds), Billable = g.Where(x => x.IsBillable).Sum(x => x.DurationSeconds) })
            .ToListAsync(ct);

        return users.Select(u =>
        {
            var c = completed.FirstOrDefault(x => x.UserId == u.Id)?.Count ?? 0;
            var t = time.FirstOrDefault(x => x.UserId == u.Id);
            return new ProductivityRowDto(u.Id, u.FullName, c,
                Math.Round((t?.Seconds ?? 0) / 3600.0, 2), Math.Round((t?.Billable ?? 0) / 3600.0, 2));
        }).OrderByDescending(r => r.HoursTracked).ToList();
    }

    public async Task<IReadOnlyList<CalendarItemDto>> GetCalendarAsync(DateTime fromUtc, DateTime toUtc, long? projectId, CancellationToken ct = default)
    {
        var query = db.Tasks.AsNoTracking()
            .Where(t => t.DueDate != null && t.DueDate >= fromUtc && t.DueDate < toUtc);
        if (projectId is not null) query = query.Where(t => t.ProjectId == projectId);

        return await query.OrderBy(t => t.DueDate)
            .Select(t => new CalendarItemDto(t.Id, t.ProjectId, t.Title, t.Status, t.Priority, t.StartDate, t.DueDate, t.AssigneeId))
            .ToListAsync(ct);
    }
}
