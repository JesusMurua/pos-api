using POS.Domain.Helpers;
using POS.Domain.Interfaces;
using POS.Domain.Models.Catalogs;

namespace POS.Domain.Models;

/// <summary>
/// Tracks a business's Stripe subscription state, denormalized for quick access.
/// </summary>
public class Subscription : IBusinessScoped
{
    public int Id { get; set; }
    public int BusinessId { get; set; }
    public Business Business { get; set; } = null!;

    // Stripe references
    public string StripeCustomerId { get; set; } = null!;
    public string StripeSubscriptionId { get; set; } = null!;

    /// <summary>
    /// Active and historical add-ons on this subscription (the sole add-on SSoT, OQ-5).
    /// The base plan's Stripe item id lives on <see cref="StripeBaseItemId"/>; every other
    /// Stripe item maps to a <see cref="SubscriptionAddOn"/> by <c>StripeItemId</c>.
    /// Device licenses are summed into the licensing engine via <c>EnforceDeviceLimitsAsync</c>.
    /// </summary>
    public ICollection<SubscriptionAddOn> AddOns { get; set; } = new List<SubscriptionAddOn>();

    // Plan info (denormalized for quick access)
    /// <summary>FK to PlanTypeCatalog.Id (1=Free, 2=Basic, 3=Pro, 4=Enterprise).</summary>
    public int PlanTypeId { get; set; } = PlanTypeIds.Free;
    /// <summary>Monthly | Annual</summary>
    public string BillingCycle { get; set; } = null!;
    /// <summary>General | Standard | Restaurant</summary>
    public string PricingGroup { get; set; } = null!;
    /// <summary>active | trialing | past_due | canceled | paused</summary>
    public string Status { get; set; } = null!;

    public DateTime? TrialEndsAt { get; set; }
    public DateTime CurrentPeriodStart { get; set; }
    public DateTime CurrentPeriodEnd { get; set; }
    public DateTime? CanceledAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public PlanTypeCatalog? PlanTypeCatalog { get; set; }

    // ── SaaS Billing v2 (PR-1b) ───────────────────────────────────────
    // Nullable-defer: BillingMethodId / BaseAmountCents are backfilled for
    // existing rows and stay nullable until PR-2 (which sets them on creation
    // in the worker and flips NOT NULL). No reader consumes them until then.

    /// <summary>FK → SaaSBillingMethod (the rail charging this tenant). RESTRICT. Nullable until PR-2.</summary>
    public int? BillingMethodId { get; set; }

    /// <summary>Negotiated per-tenant price in cents — the SSoT of what this tenant pays. Null = unset (e.g. Enterprise).</summary>
    public int? BaseAmountCents { get; set; }

    /// <summary>ISO 4217 currency. Default "MXN" (multi-currency is OQ-10, future).</summary>
    public string Currency { get; set; } = "MXN";

    /// <summary>When the next SaaS invoice should be generated (PR-3).</summary>
    public DateTime? NextBillingDate { get; set; }

    /// <summary>Opt-in: emit Fino's own CFDI per closed invoice (deferred integration, PR-7).</summary>
    public bool CfdiRequired { get; set; }

    /// <summary>Where invoices/receipts are sent; falls back to the owner email.</summary>
    public string? BillingEmail { get; set; }

    /// <summary>Operator-internal notes.</summary>
    public string? Notes { get; set; }

    /// <summary>Active dynamic Stripe Price backing BaseAmountCents on the Stripe rail. Populated by PR-2 (OQ-2).</summary>
    public string? StripePriceId { get; set; }

    /// <summary>Stripe base-plan item id (was on the retired SubscriptionItem). Populated by PR-2 (OQ-5).</summary>
    public string? StripeBaseItemId { get; set; }

    public SaaSBillingMethod? BillingMethod { get; set; }
}
