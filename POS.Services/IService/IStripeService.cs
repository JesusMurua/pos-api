using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Manages Stripe checkout sessions, subscription status, cancellations, and webhook processing.
/// </summary>
public interface IStripeService
{
    /// <summary>
    /// Creates a Stripe Checkout Session for a CATALOG plan (self-service) and returns
    /// the session URL. The price id is resolved server-side from the StripePlanPrice
    /// catalog by (planTypeId, billingCycle, business pricing group). Enterprise and
    /// businesses with an existing active subscription are rejected (contact-sales).
    /// </summary>
    Task<string> CreateCheckoutSessionAsync(int businessId, int planTypeId, string billingCycle, string successUrl, string cancelUrl);

    /// <summary>
    /// Creates a dynamic Stripe Price for a negotiated amount and returns its id.
    /// Carries metadata (planTypeId/businessId/kind=custom/cycle/group) so the webhook
    /// can resolve it without a catalog row. Idempotent per (business, amount, interval).
    /// </summary>
    Task<string> CreateCustomPriceAsync(
        int planTypeId, int businessId, long amountCents, string currency, string billingCycle, string pricingGroup);

    /// <summary>
    /// Moves a Stripe subscription's base item to a new price with proration.
    /// Idempotent per (subscription, price).
    /// </summary>
    Task UpdateSubscriptionPriceAsync(string stripeSubscriptionId, string baseItemId, string newPriceId);

    /// <summary>Archives (deactivates) a Stripe Price. Stripe prices cannot be deleted.</summary>
    Task ArchivePriceAsync(string priceId);

    /// <summary>
    /// Creates a dynamic Stripe Price for a negotiated ADD-ON amount and returns its id.
    /// Metadata kind="custom-addon" (+ addOnId/businessId) lets the webhook classify it.
    /// Idempotent per (business, addOn, amount, interval).
    /// </summary>
    Task<string> CreateAddOnPriceAsync(
        int planAddOnId, int businessId, long amountCents, string currency, string billingCycle);

    /// <summary>
    /// Appends a price item to a Stripe subscription with proration and returns the new
    /// Stripe item id (<c>si_…</c>). Idempotent per (subscription, price).
    /// </summary>
    Task<string> AddSubscriptionItemAsync(string stripeSubscriptionId, string priceId, int quantity);

    /// <summary>Removes a Stripe subscription item (add-on) with proration.</summary>
    Task RemoveSubscriptionItemAsync(string stripeSubscriptionItemId);

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
