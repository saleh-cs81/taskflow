using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.TimeTracking;

public record StartTimerRequest(long ProjectId, long? TaskId, string? Note, bool IsBillable);

public record ManualTimeEntryRequest(
    long ProjectId,
    long? TaskId,
    DateTime StartUtc,
    DateTime EndUtc,
    string? Note,
    bool IsBillable);

public record UpdateTimeEntryRequest(
    long ProjectId,
    long? TaskId,
    DateTime StartUtc,
    DateTime EndUtc,
    string? Note,
    bool IsBillable);

public record TimeEntryDto(
    long Id,
    long UserId,
    long ProjectId,
    long? TaskId,
    DateTime StartUtc,
    DateTime? EndUtc,
    int DurationSeconds,
    double DurationHours,
    bool IsRunning,
    bool IsBillable,
    string? Note,
    TimeEntrySource Source);

public record TimeSummaryDto(
    DateTime FromUtc,
    DateTime ToUtc,
    int TotalSeconds,
    double TotalHours,
    double BillableHours,
    double NonBillableHours,
    IReadOnlyList<ProjectTimeBreakdown> ByProject);

public record ProjectTimeBreakdown(long ProjectId, double Hours, double BillableHours);

public record CreateTimesheetRequest(DateTime PeriodStart, DateTime PeriodEnd);
public record ReviewTimesheetRequest(string? ReviewNote);

public record TimesheetDto(
    long Id,
    long UserId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    TimesheetStatus Status,
    double TotalHours,
    DateTime? SubmittedUtc,
    DateTime? ApprovedUtc,
    long? ApprovedById,
    string? ReviewNote);
