namespace TaskFlow.Domain.Enums;

public enum TimesheetStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Rejected = 3
}

public enum TimeEntrySource
{
    Timer = 0,
    Manual = 1
}
