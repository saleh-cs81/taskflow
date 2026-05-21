using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Entities;

public class RefreshToken : TenantEntity
{
    public long UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? DeviceInfo { get; set; }
    public string? CreatedByIp { get; set; }

    public bool IsActive => RevokedUtc is null && DateTime.UtcNow < ExpiresUtc;

    public User User { get; set; } = null!;
}
