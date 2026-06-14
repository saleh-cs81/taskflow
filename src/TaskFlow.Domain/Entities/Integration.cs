using TaskFlow.Domain.Common;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Domain.Entities;

// One Paymo connection per tenant. API key stored encrypted.
public class PaymoConnection : TenantEntity
{
    public string ApiKeyEncrypted { get; set; } = string.Empty;
    public PaymoConnectionStatus Status { get; set; } = PaymoConnectionStatus.Disconnected;
    public DateTime? LastSyncUtc { get; set; }
}

public class MigrationJob : TenantEntity
{
    public MigrationJobType Type { get; set; }
    public MigrationJobStatus Status { get; set; } = MigrationJobStatus.Pending;
    public int TotalRecords { get; set; }
    public int ProcessedRecords { get; set; }
    public int ErrorCount { get; set; }
    // Outer-loop progress so the UI can render a percentage while running.
    public int ProjectsTotal { get; set; }
    public int ProjectsDone { get; set; }
    // When set, this run imports only the next N not-yet-imported projects (staged migration). null = all.
    public int? BatchSize { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public string? Message { get; set; }
}

// Catalog of every Paymo project + its per-project migration status. Drives the staging page
// (which projects are imported vs pending/failed) and batched migration (pick the next N pending).
public class MigrationProjectItem : TenantEntity
{
    public long PaymoProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Seq { get; set; }                       // order from Paymo's project list
    public MigrationProjectStatus Status { get; set; } = MigrationProjectStatus.Pending;
    public long? LocalProjectId { get; set; }
    public int RecordCount { get; set; }               // records imported for this project (tasks, time, etc.)
    public string? ErrorMessage { get; set; }
    public DateTime? ImportedAtUtc { get; set; }
}

// Maps a Paymo record to its local counterpart. Backbone of idempotency,
// duplicate prevention, and incremental sync.
public class EntityMapping : TenantEntity
{
    public string EntityType { get; set; } = string.Empty; // "Project", "TaskList", "Task", "TimeEntry"
    public long PaymoId { get; set; }
    public long LocalId { get; set; }
    public DateTime LastSyncedUtc { get; set; }
}

public class MigrationError : TenantEntity
{
    public long MigrationJobId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public long? PaymoId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? PayloadJson { get; set; }
    public int RetryCount { get; set; }
}
