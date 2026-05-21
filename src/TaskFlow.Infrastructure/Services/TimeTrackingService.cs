using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.TimeTracking;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Services;

public class TimeTrackingService(IAppDbContext db, ICurrentUser currentUser, IDateTime clock) : ITimeTrackingService
{
    private long CurrentUserId => currentUser.UserId ?? throw new UnauthorizedAppException("error.unauthorized");

    public async Task<TimeEntryDto> StartTimerAsync(StartTimerRequest r, CancellationToken ct = default)
    {
        if (!await db.Projects.AnyAsync(p => p.Id == r.ProjectId, ct))
            throw new NotFoundAppException("error.not_found");

        // Stop any timer already running for this user before starting a new one.
        await StopRunningInternal(ct);

        var now = clock.UtcNow;
        var entry = new TimeEntry
        {
            UserId = CurrentUserId,
            ProjectId = r.ProjectId,
            TaskId = r.TaskId,
            StartUtc = now,
            IsRunning = true,
            IsBillable = r.IsBillable,
            Note = r.Note,
            Source = TimeEntrySource.Timer
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return ToDto(entry);
    }

    public async Task<TimeEntryDto> StopTimerAsync(CancellationToken ct = default)
    {
        var entry = await StopRunningInternal(ct)
            ?? throw new NotFoundAppException("error.not_found");
        await db.SaveChangesAsync(ct);
        return ToDto(entry);
    }

    public async Task<TimeEntryDto?> GetRunningAsync(CancellationToken ct = default)
    {
        var uid = CurrentUserId;
        var entry = await db.TimeEntries.AsNoTracking()
            .FirstOrDefaultAsync(e => e.UserId == uid && e.IsRunning, ct);
        return entry is null ? null : ToDto(entry);
    }

    public async Task<TimeEntryDto> AddManualAsync(ManualTimeEntryRequest r, CancellationToken ct = default)
    {
        if (r.EndUtc <= r.StartUtc) throw new ValidationAppException("error.validation");
        if (!await db.Projects.AnyAsync(p => p.Id == r.ProjectId, ct))
            throw new NotFoundAppException("error.not_found");

        var entry = new TimeEntry
        {
            UserId = CurrentUserId,
            ProjectId = r.ProjectId,
            TaskId = r.TaskId,
            StartUtc = r.StartUtc,
            EndUtc = r.EndUtc,
            DurationSeconds = (int)(r.EndUtc - r.StartUtc).TotalSeconds,
            IsRunning = false,
            IsBillable = r.IsBillable,
            Note = r.Note,
            Source = TimeEntrySource.Manual
        };
        db.TimeEntries.Add(entry);
        await db.SaveChangesAsync(ct);
        return ToDto(entry);
    }

    public async Task<TimeEntryDto> UpdateAsync(long id, UpdateTimeEntryRequest r, CancellationToken ct = default)
    {
        if (r.EndUtc <= r.StartUtc) throw new ValidationAppException("error.validation");
        var entry = await OwnedEntry(id, ct);

        entry.ProjectId = r.ProjectId;
        entry.TaskId = r.TaskId;
        entry.StartUtc = r.StartUtc;
        entry.EndUtc = r.EndUtc;
        entry.DurationSeconds = (int)(r.EndUtc - r.StartUtc).TotalSeconds;
        entry.IsRunning = false;
        entry.IsBillable = r.IsBillable;
        entry.Note = r.Note;
        await db.SaveChangesAsync(ct);
        return ToDto(entry);
    }

    public async Task DeleteAsync(long id, CancellationToken ct = default)
    {
        var entry = await OwnedEntry(id, ct);
        db.TimeEntries.Remove(entry);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<TimeEntryDto>> ListAsync(DateTime fromUtc, DateTime toUtc, long? projectId, long? userId, CancellationToken ct = default)
    {
        var query = db.TimeEntries.AsNoTracking()
            .Where(e => e.StartUtc >= fromUtc && e.StartUtc < toUtc);
        query = userId is not null ? query.Where(e => e.UserId == userId) : query.Where(e => e.UserId == CurrentUserId);
        if (projectId is not null) query = query.Where(e => e.ProjectId == projectId);

        var entries = await query.OrderByDescending(e => e.StartUtc).ToListAsync(ct);
        return entries.Select(ToDto).ToList();
    }

    public async Task<TimeSummaryDto> SummaryAsync(DateTime fromUtc, DateTime toUtc, long? userId, CancellationToken ct = default)
    {
        var uid = userId ?? CurrentUserId;
        var entries = await db.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == uid && e.StartUtc >= fromUtc && e.StartUtc < toUtc && !e.IsRunning)
            .Select(e => new { e.ProjectId, e.DurationSeconds, e.IsBillable })
            .ToListAsync(ct);

        var totalSeconds = entries.Sum(e => e.DurationSeconds);
        var billable = entries.Where(e => e.IsBillable).Sum(e => e.DurationSeconds) / 3600.0;
        var nonBillable = entries.Where(e => !e.IsBillable).Sum(e => e.DurationSeconds) / 3600.0;

        var byProject = entries.GroupBy(e => e.ProjectId)
            .Select(g => new ProjectTimeBreakdown(
                g.Key,
                Math.Round(g.Sum(x => x.DurationSeconds) / 3600.0, 2),
                Math.Round(g.Where(x => x.IsBillable).Sum(x => x.DurationSeconds) / 3600.0, 2)))
            .OrderByDescending(p => p.Hours).ToList();

        return new TimeSummaryDto(fromUtc, toUtc, totalSeconds,
            Math.Round(totalSeconds / 3600.0, 2), Math.Round(billable, 2), Math.Round(nonBillable, 2), byProject);
    }

    public async Task<IReadOnlyList<TimesheetDto>> ListTimesheetsAsync(CancellationToken ct = default)
    {
        var uid = CurrentUserId;
        var sheets = await db.Timesheets.AsNoTracking()
            .Where(t => t.UserId == uid)
            .OrderByDescending(t => t.PeriodStart).ToListAsync(ct);

        var result = new List<TimesheetDto>(sheets.Count);
        foreach (var s in sheets)
            result.Add(await ToTimesheetDto(s, ct));
        return result;
    }

    public async Task<TimesheetDto> CreateTimesheetAsync(CreateTimesheetRequest r, CancellationToken ct = default)
    {
        if (r.PeriodEnd <= r.PeriodStart) throw new ValidationAppException("error.validation");
        var sheet = new Timesheet
        {
            UserId = CurrentUserId,
            PeriodStart = r.PeriodStart,
            PeriodEnd = r.PeriodEnd,
            Status = TimesheetStatus.Draft
        };
        db.Timesheets.Add(sheet);
        await db.SaveChangesAsync(ct);
        return await ToTimesheetDto(sheet, ct);
    }

    public async Task<TimesheetDto> SubmitTimesheetAsync(long id, CancellationToken ct = default)
    {
        var sheet = await OwnedTimesheet(id, ct);
        if (sheet.Status is not (TimesheetStatus.Draft or TimesheetStatus.Rejected))
            throw new ConflictAppException("error.conflict");
        sheet.Status = TimesheetStatus.Submitted;
        sheet.SubmittedUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return await ToTimesheetDto(sheet, ct);
    }

    public async Task<TimesheetDto> ApproveTimesheetAsync(long id, ReviewTimesheetRequest r, CancellationToken ct = default)
        => await ReviewAsync(id, TimesheetStatus.Approved, r.ReviewNote, ct);

    public async Task<TimesheetDto> RejectTimesheetAsync(long id, ReviewTimesheetRequest r, CancellationToken ct = default)
        => await ReviewAsync(id, TimesheetStatus.Rejected, r.ReviewNote, ct);

    private async Task<TimesheetDto> ReviewAsync(long id, TimesheetStatus status, string? note, CancellationToken ct)
    {
        // Approval may target another user's timesheet; query without the "owned" guard.
        var sheet = await db.Timesheets.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundAppException("error.not_found");
        if (sheet.Status != TimesheetStatus.Submitted)
            throw new ConflictAppException("error.conflict");

        sheet.Status = status;
        sheet.ReviewNote = note;
        sheet.ApprovedById = CurrentUserId;
        sheet.ApprovedUtc = status == TimesheetStatus.Approved ? clock.UtcNow : null;
        await db.SaveChangesAsync(ct);
        return await ToTimesheetDto(sheet, ct);
    }

    // --- helpers ---

    private async Task<TimeEntry?> StopRunningInternal(CancellationToken ct)
    {
        var uid = CurrentUserId;
        var running = await db.TimeEntries.Where(e => e.UserId == uid && e.IsRunning).ToListAsync(ct);
        TimeEntry? last = null;
        var now = clock.UtcNow;
        foreach (var e in running)
        {
            e.EndUtc = now;
            e.DurationSeconds = (int)(now - e.StartUtc).TotalSeconds;
            e.IsRunning = false;
            last = e;
        }
        return last;
    }

    private async Task<TimeEntry> OwnedEntry(long id, CancellationToken ct)
    {
        var uid = CurrentUserId;
        return await db.TimeEntries.FirstOrDefaultAsync(e => e.Id == id && e.UserId == uid, ct)
            ?? throw new NotFoundAppException("error.not_found");
    }

    private async Task<Timesheet> OwnedTimesheet(long id, CancellationToken ct)
    {
        var uid = CurrentUserId;
        return await db.Timesheets.FirstOrDefaultAsync(t => t.Id == id && t.UserId == uid, ct)
            ?? throw new NotFoundAppException("error.not_found");
    }

    private async Task<TimesheetDto> ToTimesheetDto(Timesheet s, CancellationToken ct)
    {
        var seconds = await db.TimeEntries.AsNoTracking()
            .Where(e => e.UserId == s.UserId && !e.IsRunning && e.StartUtc >= s.PeriodStart && e.StartUtc < s.PeriodEnd)
            .SumAsync(e => (int?)e.DurationSeconds, ct) ?? 0;

        return new TimesheetDto(s.Id, s.UserId, s.PeriodStart, s.PeriodEnd, s.Status,
            Math.Round(seconds / 3600.0, 2), s.SubmittedUtc, s.ApprovedUtc, s.ApprovedById, s.ReviewNote);
    }

    private static TimeEntryDto ToDto(TimeEntry e) => new(
        e.Id, e.UserId, e.ProjectId, e.TaskId, e.StartUtc, e.EndUtc, e.DurationSeconds,
        Math.Round(e.DurationSeconds / 3600.0, 2), e.IsRunning, e.IsBillable, e.Note, e.Source);
}
