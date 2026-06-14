using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Common.Interfaces;

// A queued migration run. Captures tenant + user explicitly because the work
// executes on a background thread with no HttpContext.
// BatchSize: when set, import only the next N not-yet-imported projects; null = all remaining.
public record MigrationWorkItem(long JobId, long TenantId, long UserId, MigrationJobType Type, int? BatchSize = null);

public interface IMigrationQueue
{
    void Enqueue(MigrationWorkItem item);
    IAsyncEnumerable<MigrationWorkItem> DequeueAllAsync(CancellationToken ct);
}
