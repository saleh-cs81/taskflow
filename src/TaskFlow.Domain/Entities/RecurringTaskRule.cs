using TaskFlow.Domain.Common;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Domain.Entities;

public enum RecurrenceUnit
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2
}

// A template that generates task instances on a schedule.
public class RecurringTaskRule : TenantEntity
{
    public long ProjectId { get; set; }
    public long? TaskListId { get; set; }

    public string TitleTemplate { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TaskPriority Priority { get; set; } = TaskPriority.Normal;
    public long? AssigneeId { get; set; }

    public RecurrenceUnit Unit { get; set; } = RecurrenceUnit.Weekly;
    public int Interval { get; set; } = 1;          // every N units
    public int? DueInDays { get; set; }             // generated task due date offset from creation

    public bool IsActive { get; set; } = true;
    public DateTime NextRunUtc { get; set; }
    public DateTime? LastRunUtc { get; set; }

    public DateTime Advance(DateTime from) => Unit switch
    {
        RecurrenceUnit.Daily => from.AddDays(Interval),
        RecurrenceUnit.Weekly => from.AddDays(7 * Interval),
        RecurrenceUnit.Monthly => from.AddMonths(Interval),
        _ => from.AddDays(Interval)
    };
}
