using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Common.Interfaces;

// A queued migration run. Captures tenant + user explicitly because the work
// executes on a background thread with no HttpContext.
// BatchSize: when set (Batch mode), import only the next N not-yet-imported projects.
// Mode + TargetProjectId select what the run does (base data only / one project / batch / full).
public record MigrationWorkItem(
    long JobId, long TenantId, long UserId, MigrationJobType Type,
    int? BatchSize = null, MigrationMode Mode = MigrationMode.Full, long? TargetProjectId = null);

public interface IMigrationQueue
{
    void Enqueue(MigrationWorkItem item);
    IAsyncEnumerable<MigrationWorkItem> DequeueAllAsync(CancellationToken ct);
}
