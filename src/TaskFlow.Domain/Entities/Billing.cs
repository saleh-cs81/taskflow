using TaskFlow.Domain.Common;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Domain.Entities;

// Global catalogue of subscription plans (not tenant-scoped).
public class Plan : BaseEntity
{
    public string Code { get; set; } = string.Empty;   // "free", "pro", "business"
    public string Name { get; set; } = string.Empty;
    public decimal PriceMonthly { get; set; }
    public string Currency { get; set; } = "USD";
    public int MaxUsers { get; set; }                   // -1 = unlimited
    public int MaxProjects { get; set; }                // -1 = unlimited
    public string? FeaturesJson { get; set; }
    public int SortOrder { get; set; }
}

public class Subscription : TenantEntity
{
    public long PlanId { get; set; }
    public string Provider { get; set; } = "PayPal";
    public string? ProviderSubscriptionId { get; set; }
    public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Trial;
    public DateTime? CurrentPeriodEndUtc { get; set; }

    public Plan Plan { get; set; } = null!;
}
