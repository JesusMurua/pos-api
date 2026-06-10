using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models.Catalogs;

/// <summary>
/// DB-backed catalog of the Stripe Price ids for each (PlanType × BillingCycle ×
/// PricingGroup) combination — the durable replacement of the static
/// <c>StripeConstants.PriceMap</c> (PR-2, OQ-2). Custom negotiated prices are NOT
/// stored here (they live on <see cref="Subscription.StripePriceId"/> and carry
/// <c>metadata.planTypeId</c> on the Stripe object). The webhook resolves a base
/// price as: catalog → custom-metadata → fail-closed. See
/// docs/saas-billing-architecture.md §5.
/// </summary>
public class StripePlanPrice
{
    public int Id { get; set; }

    /// <summary>FK → PlanTypeCatalog.Id.</summary>
    public int PlanTypeId { get; set; }

    /// <summary>"Monthly" | "Annual".</summary>
    [Required, MaxLength(20)]
    public string BillingCycle { get; set; } = null!;

    /// <summary>"General" | "Standard" | "Restaurant".</summary>
    [Required, MaxLength(20)]
    public string PricingGroup { get; set; } = null!;

    /// <summary>The Stripe Price id (e.g. "price_1...."). Unique.</summary>
    [Required, MaxLength(64)]
    public string StripePriceId { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}
