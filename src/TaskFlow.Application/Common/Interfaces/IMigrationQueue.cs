using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Common.Interfaces;

// A queued migration run. Captures tenant + user explicitly because the work
// executes on a background thread with no HttpContext.
public record MigrationWorkItem(long JobId, long TenantId, long UserId, MigrationJobType Type);

public interface IMigrationQueue
{
    void Enqueue(MigrationWorkItem item);
    IAsyncEnumerable<MigrationWorkItem> DequeueAllAsync(CancellationToken ct);
}
