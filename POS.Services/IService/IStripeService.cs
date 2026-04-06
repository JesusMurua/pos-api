using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Manages Stripe checkout sessions, subscription status, cancellations, and webhook processing.
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

    /// <summary>
    /// Queues a Stripe webhook event for background processing.
    /// Silently ignores duplicate events (same StripeEventId).
    /// </summary>
    /// <param name="stripeEventId">The Stripe event ID.</param>
    /// <param name="eventType">The event type (e.g., "checkout.session.completed").</param>
    /// <param name="rawJson">The full JSON payload.</param>
    Task QueueWebhookEventAsync(string stripeEventId, string eventType, string rawJson);
}
