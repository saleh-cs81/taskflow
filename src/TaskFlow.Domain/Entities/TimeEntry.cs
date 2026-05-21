using TaskFlow.Domain.Common;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Domain.Entities;

public class TimeEntry : TenantEntity
{
    public long UserId { get; set; }
    public long ProjectId { get; set; }
    public long? TaskId { get; set; }

    public DateTime StartUtc { get; set; }
    public DateTime? EndUtc { get; set; }
    public int DurationSeconds { get; set; }

    public bool IsRunning { get; set; }
    public bool IsBillable { get; set; }
    public string? Note { get; set; }
    public TimeEntrySource Source { get; set; } = TimeEntrySource.Manual;

    public Project Project { get; set; } = null!;
    public TaskItem? Task { get; set; }
    public User User { get; set; } = null!;
}

// A period (e.g. a week) a user submits for approval. Entries are matched by user + date range.
public class Timesheet : TenantEntity
{
    public long UserId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public TimesheetStatus Status { get; set; } = TimesheetStatus.Draft;

    public DateTime? SubmittedUtc { get; set; }
    public DateTime? ApprovedUtc { get; set; }
    public long? ApprovedById { get; set; }
    public string? ReviewNote { get; set; }

    public User User { get; set; } = null!;
}
