using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskFlow.Application.Features.Billing;

namespace TaskFlow.API.Controllers;

[ApiController]
[Route("api/v1/billing")]
[Authorize]
public class BillingController(IBillingService billing) : ControllerBase
{
    [HttpGet("plans")]
    public async Task<ActionResult<IReadOnlyList<PlanDto>>> Plans(CancellationToken ct)
        => Ok(await billing.ListPlansAsync(ct));

    [HttpGet("subscription")]
    public async Task<ActionResult<SubscriptionDto>> Current(CancellationToken ct)
        => Ok(await billing.GetCurrentAsync(ct));

    [HttpGet("usage")]
    public async Task<ActionResult<UsageDto>> Usage(CancellationToken ct)
        => Ok(await billing.GetUsageAsync(ct));

    // Returns the PayPal approval URL to redirect the user to.
    [HttpPost("subscribe")]
    [Authorize(Roles = "CompanyAdmin,SuperAdmin")]
    public async Task<ActionResult<SubscribeResult>> Subscribe(SubscribeRequest request, CancellationToken ct)
        => Ok(await billing.SubscribeAsync(request, ct));

    // Called after PayPal approval (or by webhook) to activate the subscription.
    [HttpPost("confirm")]
    [Authorize(Roles = "CompanyAdmin,SuperAdmin")]
    public async Task<ActionResult<SubscriptionDto>> Confirm(ConfirmSubscriptionRequest request, CancellationToken ct)
        => Ok(await billing.ConfirmAsync(request, ct));
}
