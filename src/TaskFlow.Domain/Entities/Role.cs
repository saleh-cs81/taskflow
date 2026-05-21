using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Entities;

// A role is tenant-scoped. System roles are seeded per tenant on creation.
public class Role : TenantEntity
{
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }

    // System roles cannot be deleted/renamed by tenant admins.
    public bool IsSystemRole { get; set; }

    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
