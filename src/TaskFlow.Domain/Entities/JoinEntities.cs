namespace TaskFlow.Domain.Entities;

public class UserRole
{
    public long UserId { get; set; }
    public long RoleId { get; set; }
    public User User { get; set; } = null!;
    public Role Role { get; set; } = null!;
}

public class RolePermission
{
    public long RoleId { get; set; }
    public long PermissionId { get; set; }
    public Role Role { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}
