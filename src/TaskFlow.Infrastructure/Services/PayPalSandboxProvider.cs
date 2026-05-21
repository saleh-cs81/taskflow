using TaskFlow.Application.Common.Interfaces;

namespace TaskFlow.Infrastructure.Services;

// Sandbox PayPal provider for local/dev. Real implementation would call
// PayPal's /v1/billing/subscriptions API and return the live approval link.
public class PayPalSandboxProvider : IPaymentProvider
{
    public string Name => "PayPal";

    // Per product preference: manage subscriptions on PayPal's autopay page.
    public string ManageUrl => "https://www.paypal.com/myaccount/autopay/";

    public Task<SubscriptionCreation> CreateSubscriptionAsync(string planCode, decimal priceMonthly, string currency, CancellationToken ct = default)
    {
        var subId = $"SANDBOX-{planCode}-{Guid.NewGuid():N}"[..32];
        // In sandbox, the "approval" returns to our confirm endpoint with the sub id.
        var approvalUrl = $"/billing.html?approved={subId}";
        return Task.FromResult(new SubscriptionCreation(subId, approvalUrl));
    }
}
