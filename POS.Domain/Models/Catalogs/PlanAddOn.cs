using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Data-driven catalog of billable add-ons sold on top of a base plan (extra device
/// licenses, payment-method rails, branch slots…). DB-backed replacement of the retired
/// static <c>StripeConstants.AddonPriceMap</c>. Same paradigm as <c>SaaSBillingMethod</c>
/// / <c>StripePlanPrice</c>. See docs/saas-billing-architecture.md §4.8 (source of truth).
/// </summary>
public class PlanAddOn
{
    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(60)]
    public string Name { get; set; } = null!;

    [MaxLength(300)]
    public string? Description { get; set; }

    public PlanAddOnBillingCycle BillingCycle { get; set; }

    public int DefaultPriceCents { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "MXN";

    public PlanAddOnLinkType LinkType { get; set; }

    /// <summary>
    /// Meaning depends on <see cref="LinkType"/>. For <c>DeviceLicense</c> it is the
    /// integer value of a <see cref="FeatureKey"/> (no FK — the column is polymorphic).
    /// </summary>
    public int? LinkedEntityId { get; set; }

    /// <summary>
    /// The Stripe Price that materializes this add-on on the Stripe rail. Nullable:
    /// manual-rail add-ons need no Price; a negotiated amount creates a custom Price at
    /// activation (metadata kind="custom-addon").
    /// </summary>
    [MaxLength(64)]
    public string? StripePriceId { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>True for system-seeded add-ons — code-owned, never hard-deletable.</summary>
    public bool IsSystem { get; set; }
}
