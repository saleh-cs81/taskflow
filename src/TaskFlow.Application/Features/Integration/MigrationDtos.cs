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

// One row on the staging page: a Paymo project and whether it's been migrated.
public record MigrationProjectItemDto(
    long PaymoProjectId,
    string Name,
    string? Code,
    MigrationProjectStatus Status,
    int RecordCount,
    string? ErrorMessage,
    DateTime? ImportedAtUtc);

public record MigrationCatalogDto(
    int Total,
    int Imported,
    int Failed,
    int Pending,
    IReadOnlyList<MigrationProjectItemDto> Items);

public interface IMigrationService : IMigrationExecutor
{
    Task<PaymoConnectionDto> GetStatusAsync(CancellationToken ct = default);
    Task<PaymoConnectionDto> ConnectAsync(ConnectPaymoRequest request, CancellationToken ct = default);

    // Queues a full or incremental migration and returns the created job id immediately.
    // The actual import runs in the background; poll GetJobAsync for live progress.
    Task<StartMigrationResult> StartAsync(MigrationJobType type, CancellationToken ct = default);

    // Staged migration: import only the next `count` not-yet-imported projects (count <= 0 => all remaining).
    Task<StartMigrationResult> StartBatchAsync(int count, CancellationToken ct = default);

    // Step 1 — migrate users only (also builds the project catalog).
    Task<StartMigrationResult> StartUsersAsync(CancellationToken ct = default);

    // Step 2 — migrate clients (+ contacts + client-level financials).
    Task<StartMigrationResult> StartClientsAsync(CancellationToken ct = default);

    // Migrate one specific project, fully (self-contained).
    Task<StartMigrationResult> StartProjectAsync(long paymoProjectId, CancellationToken ct = default);

    // Request a graceful stop of the running migration.
    Task StopAsync(CancellationToken ct = default);

    // The per-project staging catalog (which projects are imported / pending / failed).
    Task<MigrationCatalogDto> GetProjectItemsAsync(CancellationToken ct = default);

    // Clears previously-imported data (projects/lists/tasks/time entries/users) + mappings + jobs
    // so a migration can be re-run from a clean slate. Only touches imported records.
    Task ResetAsync(CancellationToken ct = default);

    Task<MigrationJobDto> GetJobAsync(long jobId, CancellationToken ct = default);
    Task<MigrationJobDto?> GetLatestJobAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MigrationJobDto>> ListJobsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MigrationErrorDto>> ListErrorsAsync(long jobId, CancellationToken ct = default);
}
