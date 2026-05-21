namespace TaskFlow.Domain.Common;

public abstract class BaseEntity : IEntity, IAuditable
{
    public long Id { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public long? CreatedById { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public long? UpdatedById { get; set; }

    // Optimistic concurrency token (SQL Server rowversion).
    public byte[]? RowVersion { get; set; }
}

// Base for tenant-scoped, soft-deletable entities. Most domain tables inherit this.
public abstract class TenantEntity : BaseEntity, ITenantOwned, ISoftDeletable
{
    public long TenantId { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public long? DeletedById { get; set; }
}
