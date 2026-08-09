using TaskFlow.Application.Common.Models;

namespace TaskFlow.Application.Features.Audit;

// One row of the system transaction / audit log.
public record AuditLogDto(
    long Id,
    long? UserId,
    string? UserName,
    string TableName,
    string RecordId,
    string ChangeType,          // Created | Updated | Deleted | Login | Logout | LoginFailed | PasswordChanged
    string? OldValuesJson,
    string? NewValuesJson,
    DateTime CreatedAtUtc);

public record AuditFilter(
    DateTime? From = null,
    DateTime? To = null,
    long? UserId = null,
    string? TableName = null,
    int? ChangeType = null,
    string? Search = null);

public interface IAuditService
{
    Task<PagedResult<AuditLogDto>> ListAsync(PageQuery page, AuditFilter filter, CancellationToken ct = default);

    // Distinct entity/table names present in the log (for the filter dropdown).
    Task<IReadOnlyList<string>> DistinctTablesAsync(CancellationToken ct = default);
}
