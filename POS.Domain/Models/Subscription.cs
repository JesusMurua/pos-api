using POS.Domain.Helpers;
using POS.Domain.Models.Catalogs;

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
    /// <summary>FK to PlanTypeCatalog.Id (1=Free, 2=Basic, 3=Pro, 4=Enterprise).</summary>
    public int PlanTypeId { get; set; } = PlanTypeIds.Free;
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

    public PlanTypeCatalog? PlanTypeCatalog { get; set; }
}
