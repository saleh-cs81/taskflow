using TaskFlow.Domain.Common;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Domain.Entities;

// The Company / organization. Root of the multi-tenant hierarchy.
// NOTE: Tenant itself is NOT TenantEntity (it is the tenant), but it is soft-deletable.
public class Tenant : BaseEntity, ISoftDeletable
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }

    public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Trial;
    public DateTime? TrialEndsUtc { get; set; }

    // Default locale for new users: "en" or "ar".
    public string DefaultLocale { get; set; } = "en";
    public string TimeZone { get; set; } = "UTC";

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAtUtc { get; set; }
    public long? DeletedById { get; set; }

    public ICollection<User> Users { get; set; } = new List<User>();
    public ICollection<Role> Roles { get; set; } = new List<Role>();
}
