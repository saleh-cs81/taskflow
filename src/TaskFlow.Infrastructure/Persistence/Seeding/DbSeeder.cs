using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Persistence.Seeding;

public static class DbSeeder
{
    // Runs at startup: applies migrations and seeds the global permission catalogue.
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await db.Database.MigrateAsync(ct);

        var existingCodes = await db.Permissions.Select(p => p.Code).ToListAsync(ct);
        var missing = Permissions.All.Where(p => !existingCodes.Contains(p.Code)).ToList();
        if (missing.Count > 0)
        {
            db.Permissions.AddRange(missing.Select(p => new Permission { Code = p.Code, Group = p.Group }));
            await db.SaveChangesAsync(ct);
        }

        await SeedPlansAsync(db, ct);
        await ResumeIncompleteMigrationsAsync(db, scope.ServiceProvider.GetRequiredService<IMigrationQueue>(), ct);
    }

    // A Paymo import runs as an in-process background job; if the app pool recycles mid-run the job is
    // left Running with its in-memory queue lost. On startup, re-enqueue the newest unfinished job per
    // tenant so the import self-heals and resumes (fast-skipping already-completed work via mappings).
    private static async Task ResumeIncompleteMigrationsAsync(AppDbContext db, IMigrationQueue queue, CancellationToken ct)
    {
        var incomplete = await db.MigrationJobs.IgnoreQueryFilters()
            .Where(j => j.Status == MigrationJobStatus.Running || j.Status == MigrationJobStatus.Pending)
            .OrderByDescending(j => j.Id)
            .ToListAsync(ct);
        if (incomplete.Count == 0) return;

        var resumedTenants = new HashSet<long>();
        foreach (var job in incomplete)
        {
            if (resumedTenants.Contains(job.TenantId))
            {
                job.Status = MigrationJobStatus.Failed;
                job.Message = "Superseded on restart by a newer run.";
                continue;
            }
            var userId = job.CreatedById
                ?? await db.Users.IgnoreQueryFilters().Where(u => u.TenantId == job.TenantId)
                       .Select(u => (long?)u.Id).FirstOrDefaultAsync(ct);
            if (userId is not { } uid) continue;   // no user to attribute the run to
            resumedTenants.Add(job.TenantId);
            queue.Enqueue(new MigrationWorkItem(job.Id, job.TenantId, uid, job.Type));
        }
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedPlansAsync(AppDbContext db, CancellationToken ct)
    {
        var seed = new[]
        {
            new Plan { Code = "free",     Name = "Free",     PriceMonthly = 0m,  MaxUsers = 3,  MaxProjects = 3,  SortOrder = 1 },
            new Plan { Code = "pro",      Name = "Pro",      PriceMonthly = 12m, MaxUsers = 25, MaxProjects = 50, SortOrder = 2 },
            new Plan { Code = "business", Name = "Business", PriceMonthly = 39m, MaxUsers = -1, MaxProjects = -1, SortOrder = 3 },
        };
        var existing = await db.Plans.Select(p => p.Code).ToListAsync(ct);
        var toAdd = seed.Where(p => !existing.Contains(p.Code)).ToList();
        if (toAdd.Count > 0)
        {
            db.Plans.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
    }
}
