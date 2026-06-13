using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Integration;

public record ConnectPaymoRequest(string ApiKey);

public record PaymoConnectionDto(
    bool Connected,
    PaymoConnectionStatus Status,
    DateTime? LastSyncUtc);

public record MigrationJobDto(
    long Id,
    MigrationJobType Type,
    MigrationJobStatus Status,
    int TotalRecords,
    int ProcessedRecords,
    int ErrorCount,
    int ProjectsTotal,
    int ProjectsDone,
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    string? Message);

public record StartMigrationResult(long JobId);

public record MigrationErrorDto(
    long Id,
    long MigrationJobId,
    string EntityType,
    long? PaymoId,
    string Message,
    int RetryCount,
    DateTime CreatedAtUtc);

public interface IMigrationService : IMigrationExecutor
{
    Task<PaymoConnectionDto> GetStatusAsync(CancellationToken ct = default);
    Task<PaymoConnectionDto> ConnectAsync(ConnectPaymoRequest request, CancellationToken ct = default);

    // Queues a full or incremental migration and returns the created job id immediately.
    // The actual import runs in the background; poll GetJobAsync for live progress.
    Task<StartMigrationResult> StartAsync(MigrationJobType type, CancellationToken ct = default);

    // Clears previously-imported data (projects/lists/tasks/time entries/users) + mappings + jobs
    // so a migration can be re-run from a clean slate. Only touches imported records.
    Task ResetAsync(CancellationToken ct = default);

    Task<MigrationJobDto> GetJobAsync(long jobId, CancellationToken ct = default);
    Task<MigrationJobDto?> GetLatestJobAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MigrationJobDto>> ListJobsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MigrationErrorDto>> ListErrorsAsync(long jobId, CancellationToken ct = default);
}
