namespace TaskFlow.Domain.Common;

public interface IEntity
{
    long Id { get; }
}

public interface ITenantOwned
{
    long TenantId { get; set; }
}

public interface ISoftDeletable
{
    bool IsDeleted { get; set; }
    DateTime? DeletedAtUtc { get; set; }
    long? DeletedById { get; set; }
}

public interface IAuditable
{
    DateTime CreatedAtUtc { get; set; }
    long? CreatedById { get; set; }
    DateTime? UpdatedAtUtc { get; set; }
    long? UpdatedById { get; set; }
}
