namespace TaskFlow.Application.Features.TimeTracking;

public interface ITimeTrackingService
{
    // Live timer
    Task<TimeEntryDto> StartTimerAsync(StartTimerRequest request, CancellationToken ct = default);
    Task<TimeEntryDto> StopTimerAsync(CancellationToken ct = default);
    Task<TimeEntryDto?> GetRunningAsync(CancellationToken ct = default);

    // Manual entries
    Task<TimeEntryDto> AddManualAsync(ManualTimeEntryRequest request, CancellationToken ct = default);
    Task<TimeEntryDto> UpdateAsync(long id, UpdateTimeEntryRequest request, CancellationToken ct = default);
    Task DeleteAsync(long id, CancellationToken ct = default);

    Task<IReadOnlyList<TimeEntryDto>> ListAsync(DateTime fromUtc, DateTime toUtc, long? projectId, long? userId, CancellationToken ct = default);
    Task<TimeSummaryDto> SummaryAsync(DateTime fromUtc, DateTime toUtc, long? userId, CancellationToken ct = default);

    // Timesheets
    Task<IReadOnlyList<TimesheetDto>> ListTimesheetsAsync(CancellationToken ct = default);
    Task<TimesheetDto> CreateTimesheetAsync(CreateTimesheetRequest request, CancellationToken ct = default);
    Task<TimesheetDto> SubmitTimesheetAsync(long id, CancellationToken ct = default);
    Task<TimesheetDto> ApproveTimesheetAsync(long id, ReviewTimesheetRequest request, CancellationToken ct = default);
    Task<TimesheetDto> RejectTimesheetAsync(long id, ReviewTimesheetRequest request, CancellationToken ct = default);
}
