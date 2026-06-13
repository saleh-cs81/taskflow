using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Application.Features.Integration;

// Background-only entry point: runs a queued migration with explicit tenant/user
// (no HttpContext available on the worker thread).
public interface IMigrationExecutor
{
    Task ExecuteAsync(MigrationWorkItem item, CancellationToken ct = default);
}
