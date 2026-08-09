using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Integration;
using TaskFlow.Infrastructure.Persistence.Interceptors;

namespace TaskFlow.Infrastructure.Integration.Paymo;

// Drains the migration queue and runs each job in its own DI scope so the work
// continues after the HTTP request that started it has returned.
public class MigrationBackgroundService(
    IMigrationQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<MigrationBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var svc = (IMigrationExecutor)scope.ServiceProvider.GetRequiredService<IMigrationService>();
                // Don't write a per-row audit trail for the bulk import (it would add
                // hundreds of thousands of AuditLog rows). The migration is itself the record.
                using (AuditScope.Suppress())
                    await svc.ExecuteAsync(item, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Migration job {JobId} crashed in background runner", item.JobId);
            }
        }
    }
}
