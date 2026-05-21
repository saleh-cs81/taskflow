using TaskFlow.Domain.Enums;

namespace TaskFlow.Application.Features.Billing;

public record PlanDto(
    long Id,
    string Code,
    string Name,
    decimal PriceMonthly,
    string Currency,
    int MaxUsers,
    int MaxProjects);

public record SubscriptionDto(
    long? Id,
    string PlanCode,
    string PlanName,
    SubscriptionStatus Status,
    DateTime? CurrentPeriodEndUtc,
    string ManageUrl);

public record UsageDto(
    int Users, int MaxUsers,
    int Projects, int MaxProjects,
    bool OverLimit);

public record SubscribeRequest(long PlanId);
public record SubscribeResult(string ApprovalUrl);
public record ConfirmSubscriptionRequest(string ProviderSubscriptionId);

public interface IBillingService
{
    Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct = default);
    Task<SubscriptionDto> GetCurrentAsync(CancellationToken ct = default);
    Task<UsageDto> GetUsageAsync(CancellationToken ct = default);
    Task<SubscribeResult> SubscribeAsync(SubscribeRequest request, CancellationToken ct = default);
    Task<SubscriptionDto> ConfirmAsync(ConfirmSubscriptionRequest request, CancellationToken ct = default);
}
