using TaskFlow.Domain.Common;

namespace TaskFlow.Domain.Entities;

public class User : TenantEntity
{
    public string Email { get; set; } = string.Empty;
    public string NormalizedEmail { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? PhoneNumber { get; set; }

    // Per-user UI language: "en" or "ar". Drives bilingual + RTL/LTR rendering.
    public string Locale { get; set; } = "en";
    public string TimeZone { get; set; } = "UTC";

    public bool IsActive { get; set; } = true;
    public bool EmailConfirmed { get; set; }
    public DateTime? LastLoginUtc { get; set; }

    // Password reset
    public string? PasswordResetTokenHash { get; set; }
    public DateTime? PasswordResetExpiresUtc { get; set; }

    public Tenant Tenant { get; set; } = null!;
    public ICollection<UserRole> UserRoles { get; set; } = new List<UserRole>();
    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}
