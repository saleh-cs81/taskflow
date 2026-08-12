using TaskFlow.Domain.Common;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Domain.Entities;

public class Invitation : TenantEntity
{
    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public long RoleId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;
    public DateTime? AcceptedUtc { get; set; }

    public Role Role { get; set; } = null!;
}
