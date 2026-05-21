using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Recurring;

namespace TaskFlow.Infrastructure.Services;

// Periodically materializes recurring task instances that have come due.
public class RecurringTaskWorker(IServiceScopeFactory scopeFactory, ILogger<RecurringTaskWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IRecurringTaskService>();
                var clock = scope.ServiceProvider.GetRequiredService<IDateTime>();
                var count = await service.GenerateDueAsync(clock.UtcNow, stoppingToken);
                if (count > 0) logger.LogInformation("Recurring worker generated {Count} task(s)", count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Recurring task worker tick failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
