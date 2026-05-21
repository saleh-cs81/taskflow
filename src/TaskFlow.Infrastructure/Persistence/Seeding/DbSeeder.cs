using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskFlow.Application.Common.Authorization;
using TaskFlow.Domain.Entities;

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
