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
    DateTime? StartedUtc,
    DateTime? FinishedUtc,
    string? Message);

public record MigrationErrorDto(
    long Id,
    long MigrationJobId,
    string EntityType,
    long? PaymoId,
    string Message,
    int RetryCount,
    DateTime CreatedAtUtc);

public interface IMigrationService
{
    Task<PaymoConnectionDto> GetStatusAsync(CancellationToken ct = default);
    Task<PaymoConnectionDto> ConnectAsync(ConnectPaymoRequest request, CancellationToken ct = default);

    // Runs a full or incremental migration and returns the finished job summary.
    Task<MigrationJobDto> RunAsync(MigrationJobType type, CancellationToken ct = default);

    Task<MigrationJobDto> GetJobAsync(long jobId, CancellationToken ct = default);
    Task<IReadOnlyList<MigrationJobDto>> ListJobsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<MigrationErrorDto>> ListErrorsAsync(long jobId, CancellationToken ct = default);
}
