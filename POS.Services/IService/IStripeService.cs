using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Manages Stripe checkout sessions, subscription status, and cancellations.
/// </summary>
public interface IStripeService
{
    /// <summary>
    /// Creates a Stripe Checkout Session and returns the session URL.
    /// </summary>
    Task<string> CreateCheckoutSessionAsync(int businessId, string priceId, string successUrl, string cancelUrl);

    /// <summary>
    /// Returns the current Subscription entity for the business.
    /// </summary>
    Task<Subscription?> GetSubscriptionStatusAsync(int businessId);

    /// <summary>
    /// Cancels the subscription at the end of the current billing period.
    /// </summary>
    Task CancelSubscriptionAsync(int businessId);
}
