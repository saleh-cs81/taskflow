namespace TaskFlow.Application.Common.Interfaces;

public record SubscriptionCreation(string ProviderSubscriptionId, string ApprovalUrl);

// Abstraction over the billing provider (PayPal). The sandbox implementation
// returns a fake approval URL so the flow is testable without live PayPal.
public interface IPaymentProvider
{
    string Name { get; }

    // Creates a provider-side subscription and returns the approval URL the user is
    // redirected to in order to authorize recurring payment.
    Task<SubscriptionCreation> CreateSubscriptionAsync(string planCode, decimal priceMonthly, string currency, CancellationToken ct = default);

    // The URL where a user manages their existing subscription.
    string ManageUrl { get; }
}
