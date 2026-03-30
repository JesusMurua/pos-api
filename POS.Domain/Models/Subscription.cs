namespace POS.Domain.Models;

/// <summary>
/// Tracks a business's Stripe subscription state, denormalized for quick access.
/// </summary>
public class Subscription
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public Business Business { get; set; } = null!;

    // Stripe references
    public string StripeCustomerId { get; set; } = null!;
    public string StripeSubscriptionId { get; set; } = null!;
    public string StripePriceId { get; set; } = null!;

    // Plan info (denormalized for quick access)
    /// <summary>Free | Basico | Pro | Enterprise</summary>
    public string PlanType { get; set; } = null!;
    /// <summary>Monthly | Annual</summary>
    public string BillingCycle { get; set; } = null!;
    /// <summary>General | Standard | Restaurant</summary>
    public string PricingGroup { get; set; } = null!;
    /// <summary>active | trialing | past_due | canceled | paused</summary>
    public string Status { get; set; } = null!;

    public DateTime TrialEndsAt { get; set; }
    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
