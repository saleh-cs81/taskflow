using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Common.Models;
using TaskFlow.Application.Features.Audit;

namespace TaskFlow.Infrastructure.Services;

// Read side of the audit log. AuditLog is a TenantEntity, so the global query filter
// scopes results to the caller's tenant automatically.
public class AuditQueryService(IAppDbContext db) : IAuditService
{
    public async Task<PagedResult<AuditLogDto>> ListAsync(PageQuery page, AuditFilter f, CancellationToken ct = default)
    {
        var q = db.AuditLogs.AsNoTracking();

        if (f.From is { } from) q = q.Where(a => a.CreatedAtUtc >= from);
        if (f.To is { } to) q = q.Where(a => a.CreatedAtUtc < to);
        if (f.UserId is { } uid) q = q.Where(a => a.UserId == uid);
        if (!string.IsNullOrWhiteSpace(f.TableName)) q = q.Where(a => a.TableName == f.TableName);
        if (f.ChangeType is { } ctype) q = q.Where(a => (int)a.ChangeType == ctype);
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            var s = f.Search.Trim();
            q = q.Where(a =>
                a.TableName.Contains(s) ||
                a.RecordId.Contains(s) ||
                (a.NewValuesJson != null && a.NewValuesJson.Contains(s)) ||
                (a.OldValuesJson != null && a.OldValuesJson.Contains(s)));
        }

        var total = await q.CountAsync(ct);
        var rows = await q
            .OrderByDescending(a => a.CreatedAtUtc).ThenByDescending(a => a.Id)
            .Skip(page.Skip).Take(page.NormalizedSize)
            .Select(a => new
            {
                a.Id, a.UserId, a.TableName, a.RecordId, a.ChangeType,
                a.OldValuesJson, a.NewValuesJson, a.CreatedAtUtc
            })
            .ToListAsync(ct);

        // Resolve actor display names (include soft-deleted users so history stays readable).
        var ids = rows.Where(r => r.UserId != null).Select(r => r.UserId!.Value).Distinct().ToList();
        var names = ids.Count == 0
            ? new Dictionary<long, string>()
            : await db.Users.IgnoreQueryFilters()
                .Where(u => ids.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName })
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        var items = rows.Select(a => new AuditLogDto(
            a.Id,
            a.UserId,
            a.UserId != null && names.TryGetValue(a.UserId.Value, out var n) ? n : null,
            a.TableName,
            a.RecordId,
            a.ChangeType.ToString(),
            a.OldValuesJson,
            a.NewValuesJson,
            a.CreatedAtUtc)).ToList();

        return new PagedResult<AuditLogDto>(items, page.NormalizedPage, page.NormalizedSize, total);
    }

    public async Task<IReadOnlyList<string>> DistinctTablesAsync(CancellationToken ct = default)
        => await db.AuditLogs.AsNoTracking()
            .Select(a => a.TableName).Distinct().OrderBy(x => x).ToListAsync(ct);
}
