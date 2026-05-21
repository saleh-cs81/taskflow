namespace TaskFlow.Application.Common.Interfaces;

// Resolved per-request from the authenticated user's JWT claims.
public interface ITenantContext
{
    long? TenantId { get; }
    bool IsSuperAdmin { get; }
    bool HasTenant { get; }

    // Allows seeding / cross-tenant operations to bypass the global query filter.
    void SetTenant(long tenantId);
}
