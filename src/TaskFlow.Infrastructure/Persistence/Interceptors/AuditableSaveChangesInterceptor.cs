using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Domain.Common;

namespace TaskFlow.Infrastructure.Persistence.Interceptors;

// Stamps audit fields, tenant id, and converts hard deletes into soft deletes.
public class AuditableSaveChangesInterceptor(ICurrentUser currentUser, ITenantContext tenant, IDateTime clock)
    : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) Apply(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext context)
    {
        var now = clock.UtcNow;
        var userId = currentUser.UserId;

        foreach (var entry in context.ChangeTracker.Entries<IAuditable>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAtUtc = now;
                    entry.Entity.CreatedById = userId;
                    break;
                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    entry.Entity.UpdatedById = userId;
                    break;
            }
        }

        // Stamp tenant id on new tenant-owned entities.
        foreach (var entry in context.ChangeTracker.Entries<ITenantOwned>())
        {
            if (entry.State == EntityState.Added && entry.Entity.TenantId == 0 && tenant.TenantId is { } tid)
                entry.Entity.TenantId = tid;
        }

        // Convert deletes into soft deletes.
        foreach (var entry in context.ChangeTracker.Entries<ISoftDeletable>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Modified;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAtUtc = now;
                entry.Entity.DeletedById = userId;
            }
        }
    }
}
