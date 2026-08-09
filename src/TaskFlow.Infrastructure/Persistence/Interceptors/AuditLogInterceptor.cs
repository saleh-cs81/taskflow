using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Domain.Common;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Persistence.Interceptors;

// Writes an AuditLog row for every create / update / delete of a tenant-owned business entity.
// Registered BEFORE AuditableSaveChangesInterceptor so it observes the TRUE delete state
// (the auditable interceptor rewrites Deleted -> Modified for soft-deletable entities).
//
// Two-phase: capture changes in SavingChanges (keys of inserts are still temporary there),
// then in SavedChanges (keys now assigned) build the AuditLog rows and persist them with a
// second SaveChanges. That inner save re-enters this interceptor, but AuditLog is on the
// ignore list so no new work is captured and it terminates.
public class AuditLogInterceptor(ICurrentUser currentUser, ITenantContext tenant, IDateTime clock)
    : SaveChangesInterceptor
{
    // Entity CLR type names we never audit (noise / bookkeeping / secrets).
    private static readonly HashSet<string> Ignored = new(StringComparer.Ordinal)
    {
        nameof(AuditLog), nameof(ActivityLog), nameof(Notification), nameof(Mention),
        nameof(RefreshToken), nameof(EntityMapping), nameof(MigrationJob),
        nameof(MigrationError), nameof(MigrationProjectItem), nameof(PaymoConnection)
    };

    // Property names never written into the value JSON (metadata / volatile / secrets).
    private static readonly HashSet<string> SkipProps = new(StringComparer.Ordinal)
    {
        "Id", "TenantId", "RowVersion",
        "CreatedAtUtc", "CreatedById", "UpdatedAtUtc", "UpdatedById",
        "IsDeleted", "DeletedAtUtc", "DeletedById",
        "LastLoginUtc",
        "PasswordHash", "PasswordResetTokenHash", "PasswordResetExpiresUtc",
        "SecurityStamp", "TokenHash", "ReplacedByTokenHash", "CreatedByIp",
        "ApiKey", "ApiKeyEncrypted", "EncryptedApiKey", "AccessToken",
        "ClientSecret", "Secret", "RefreshToken"
    };

    private List<Captured>? _pending;

    private sealed class Captured
    {
        public required EntityEntry Entry;
        public required string TableName;
        public required AuditChangeType ChangeType;
        public long? KeyValue;
        public string? OldJson;
        public string? NewJson;
    }

    // ---- capture phase ----
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null) _pending = Capture(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is not null) _pending = Capture(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    // ---- persist phase (keys are assigned now) ----
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (eventData.Context is not null) PersistAsync(eventData.Context, default).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken ct = default)
    {
        if (eventData.Context is not null) await PersistAsync(eventData.Context, ct);
        return await base.SavedChangesAsync(eventData, result, ct);
    }

    private List<Captured>? Capture(DbContext ctx)
    {
        if (AuditScope.IsSuppressed) return null;
        List<Captured>? list = null;
        foreach (var entry in ctx.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) continue;
            if (entry.Entity is not ITenantOwned) continue;              // audit tenant business data only
            var name = entry.Metadata.ClrType.Name;
            if (Ignored.Contains(name)) continue;

            var cap = Build(entry, name);
            if (cap is not null) (list ??= new()).Add(cap);
        }
        return list;
    }

    private Captured? Build(EntityEntry entry, string name)
    {
        long? key = null;
        var idProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
        if (idProp is { IsTemporary: false, CurrentValue: long lid }) key = lid;

        switch (entry.State)
        {
            case EntityState.Added:
                return new Captured
                {
                    Entry = entry, TableName = name, ChangeType = AuditChangeType.Created, KeyValue = key,
                    NewJson = Json(Snapshot(entry, current: true))
                };

            case EntityState.Deleted:
                return new Captured
                {
                    Entry = entry, TableName = name, ChangeType = AuditChangeType.Deleted, KeyValue = key,
                    OldJson = Json(Snapshot(entry, current: false))
                };

            case EntityState.Modified:
                var oldv = new Dictionary<string, object?>();
                var newv = new Dictionary<string, object?>();
                foreach (var p in entry.Properties)
                {
                    if (!p.IsModified) continue;
                    var pn = p.Metadata.Name;
                    if (SkipProps.Contains(pn)) continue;
                    if (Equals(p.OriginalValue, p.CurrentValue)) continue;
                    oldv[pn] = Safe(p.OriginalValue);
                    newv[pn] = Safe(p.CurrentValue);
                }
                if (newv.Count == 0) return null;                        // only metadata/secrets changed
                return new Captured
                {
                    Entry = entry, TableName = name, ChangeType = AuditChangeType.Updated, KeyValue = key,
                    OldJson = Json(oldv), NewJson = Json(newv)
                };
        }
        return null;
    }

    private static Dictionary<string, object?> Snapshot(EntityEntry entry, bool current)
    {
        var d = new Dictionary<string, object?>();
        foreach (var p in entry.Properties)
        {
            var pn = p.Metadata.Name;
            if (SkipProps.Contains(pn)) continue;
            var val = current ? p.CurrentValue : p.OriginalValue;
            if (val is null) continue;
            d[pn] = Safe(val);
        }
        return d;
    }

    private static object? Safe(object? v) => v switch
    {
        null => null,
        byte[] => "<binary>",
        Enum e => e.ToString(),
        _ => v
    };

    private static string? Json(Dictionary<string, object?> d)
        => d.Count == 0 ? null : JsonSerializer.Serialize(d);

    private async ValueTask PersistAsync(DbContext ctx, CancellationToken ct)
    {
        var pending = _pending;
        _pending = null;
        if (pending is null || pending.Count == 0) return;

        var now = clock.UtcNow;
        var uid = currentUser.UserId;
        var fallbackTid = tenant.TenantId ?? 0;

        foreach (var c in pending)
        {
            long? recordId = c.KeyValue;
            if (recordId is null)
            {
                var idProp = c.Entry.Properties.FirstOrDefault(p => p.Metadata.Name == "Id");
                if (idProp?.CurrentValue is long lid) recordId = lid;
            }
            var entityTid = (c.Entry.Entity as ITenantOwned)?.TenantId ?? 0;

            ctx.Set<AuditLog>().Add(new AuditLog
            {
                TenantId = entityTid != 0 ? entityTid : fallbackTid,
                UserId = uid,
                TableName = c.TableName,
                RecordId = recordId?.ToString() ?? string.Empty,
                ChangeType = c.ChangeType,
                OldValuesJson = c.OldJson,
                NewValuesJson = c.NewJson,
                CreatedAtUtc = now,
                CreatedById = uid
            });
        }

        await ctx.SaveChangesAsync(ct);   // re-enters this interceptor; AuditLog is ignored so it stops here
    }
}
