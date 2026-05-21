using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Reports;

public record DashboardStatsDto(
    int TotalProjects,
    int ActiveProjects,
    int OpenTasks,
    int CompletedTasks,
    int OverdueTasks,
    double HoursThisWeek,
    double BillableHoursThisWeek);

public record ProjectProgressDto(
    long ProjectId,
    string ProjectName,
    ProjectStatus Status,
    int TotalTasks,
    int TodoTasks,
    int InProgressTasks,
    int DoneTasks,
    double PercentComplete);

public record ProductivityRowDto(
    long UserId,
    string FullName,
    int TasksCompleted,
    double HoursTracked,
    double BillableHours);

public record CalendarItemDto(
    long TaskId,
    long ProjectId,
    string Title,
    WorkStatus Status,
    TaskPriority Priority,
    DateTime? StartDate,
    DateTime? DueDate,
    long? AssigneeId);

public enum ReportType
{
    ProjectProgress = 0,
    Productivity = 1
}

public enum ExportFormat
{
    Csv = 0,
    Xlsx = 1,
    Pdf = 2
}

public record ExportResult(byte[] Content, string ContentType, string FileName);

public interface IReportService
{
    Task<DashboardStatsDto> GetDashboardAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProjectProgressDto>> GetProjectProgressAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ProductivityRowDto>> GetProductivityAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<IReadOnlyList<CalendarItemDto>> GetCalendarAsync(DateTime fromUtc, DateTime toUtc, long? projectId, CancellationToken ct = default);
}

public interface IReportExporter
{
    Task<ExportResult> ExportAsync(ReportType type, ExportFormat format, DateTime? fromUtc, DateTime? toUtc, CancellationToken ct = default);
}
