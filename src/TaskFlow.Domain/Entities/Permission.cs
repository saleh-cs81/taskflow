using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Entities;

// Global catalogue of permissions (not tenant-scoped). e.g. "projects.create".
public class Permission : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Group { get; set; } = string.Empty; // e.g. "Projects", "Tasks"

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
}
