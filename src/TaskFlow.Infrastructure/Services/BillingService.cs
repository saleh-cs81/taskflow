using Microsoft.EntityFrameworkCore;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Application.Common.Interfaces;
using TaskFlow.Application.Features.Billing;
using TaskFlow.Domain.Entities;
using TaskFlow.Domain.Enums;

namespace TaskFlow.Infrastructure.Services;

public class BillingService(
    IAppDbContext db,
    IPaymentProvider provider,
    ITenantContext tenant,
    IDateTime clock) : IBillingService
{
    public async Task<IReadOnlyList<PlanDto>> ListPlansAsync(CancellationToken ct = default)
        => await db.Plans.AsNoTracking().OrderBy(p => p.SortOrder)
            .Select(p => new PlanDto(p.Id, p.Code, p.Name, p.PriceMonthly, p.Currency, p.MaxUsers, p.MaxProjects))
            .ToListAsync(ct);

    public async Task<SubscriptionDto> GetCurrentAsync(CancellationToken ct = default)
    {
        var sub = await db.Subscriptions.AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc).FirstOrDefaultAsync(ct);

        if (sub is null)
        {
            var free = await FreePlanAsync(ct);
            return new SubscriptionDto(null, free.Code, free.Name, SubscriptionStatus.Trial, null, provider.ManageUrl);
        }

        var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Id == sub.PlanId, ct);
        return new SubscriptionDto(sub.Id, plan.Code, plan.Name, sub.Status, sub.CurrentPeriodEndUtc, provider.ManageUrl);
    }

    public async Task<UsageDto> GetUsageAsync(CancellationToken ct = default)
    {
        var (maxUsers, maxProjects) = await CurrentLimitsAsync(ct);
        var users = await db.Users.CountAsync(ct);
        var projects = await db.Projects.CountAsync(ct);
        var over = (maxUsers != -1 && users > maxUsers) || (maxProjects != -1 && projects > maxProjects);
        return new UsageDto(users, maxUsers, projects, maxProjects, over);
    }

    public async Task<SubscribeResult> SubscribeAsync(SubscribeRequest r, CancellationToken ct = default)
    {
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == r.PlanId, ct)
            ?? throw new NotFoundAppException("error.not_found");

        var creation = await provider.CreateSubscriptionAsync(plan.Code, plan.PriceMonthly, plan.Currency, ct);

        // Record a pending subscription; activated on confirm/webhook.
        var sub = new Subscription
        {
            TenantId = tenant.TenantId ?? throw new UnauthorizedAppException("error.unauthorized"),
            PlanId = plan.Id,
            Provider = provider.Name,
            ProviderSubscriptionId = creation.ProviderSubscriptionId,
            Status = SubscriptionStatus.Trial
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync(ct);

        return new SubscribeResult(creation.ApprovalUrl);
    }

    public async Task<SubscriptionDto> ConfirmAsync(ConfirmSubscriptionRequest r, CancellationToken ct = default)
    {
        var sub = await db.Subscriptions
            .FirstOrDefaultAsync(s => s.ProviderSubscriptionId == r.ProviderSubscriptionId, ct)
            ?? throw new NotFoundAppException("error.not_found");

        sub.Status = SubscriptionStatus.Active;
        sub.CurrentPeriodEndUtc = clock.UtcNow.AddMonths(1);

        var tenantRow = await db.Tenants.FirstOrDefaultAsync(t => t.Id == sub.TenantId, ct);
        if (tenantRow is not null) tenantRow.SubscriptionStatus = SubscriptionStatus.Active;

        await db.SaveChangesAsync(ct);

        var plan = await db.Plans.AsNoTracking().FirstAsync(p => p.Id == sub.PlanId, ct);
        return new SubscriptionDto(sub.Id, plan.Code, plan.Name, sub.Status, sub.CurrentPeriodEndUtc, provider.ManageUrl);
    }

    private async Task<(int maxUsers, int maxProjects)> CurrentLimitsAsync(CancellationToken ct)
    {
        var sub = await db.Subscriptions.AsNoTracking()
            .Where(s => s.Status == SubscriptionStatus.Active)
            .OrderByDescending(s => s.CreatedAtUtc).FirstOrDefaultAsync(ct);
        var plan = sub is not null
            ? await db.Plans.AsNoTracking().FirstAsync(p => p.Id == sub.PlanId, ct)
            : await FreePlanAsync(ct);
        return (plan.MaxUsers, plan.MaxProjects);
    }

    private async Task<Plan> FreePlanAsync(CancellationToken ct)
        => await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Code == "free", ct)
           ?? new Plan { Code = "free", Name = "Free", MaxUsers = 3, MaxProjects = 3 };
}
