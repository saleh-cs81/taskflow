using TaskFlow.Domain.Common;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Domain.Entities;

// Human-readable activity feed entries ("X created project Y").
public class ActivityLog : TenantEntity
{
    public long? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public long? EntityId { get; set; }
    public string? MetadataJson { get; set; }
}

// Immutable field-level change audit for compliance.
public class AuditLog : TenantEntity
{
    public long? UserId { get; set; }
    public string TableName { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
    public AuditChangeType ChangeType { get; set; }
    public string? OldValuesJson { get; set; }
    public string? NewValuesJson { get; set; }
}
