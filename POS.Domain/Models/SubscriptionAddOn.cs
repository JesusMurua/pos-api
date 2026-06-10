using System.ComponentModel.DataAnnotations;
using POS.Domain.Models.Catalogs;

namespace POS.Domain.Models;

/// <summary>
/// An add-on activated on a tenant's <see cref="Subscription"/> — the single SSoT for
/// add-ons (replaces the retired <c>SubscriptionItem</c>, OQ-5). Soft lifecycle:
/// activate inserts a row; deactivate sets <see cref="DeactivatedAt"/> (history kept);
/// re-activation inserts a NEW row. A partial unique index
/// <c>(SubscriptionId, AddOnId) WHERE DeactivatedAt IS NULL</c> enforces at most one
/// active instance per add-on. See docs/saas-billing-architecture.md §4.9.
/// </summary>
public class SubscriptionAddOn
{
    public int Id { get; set; }

    /// <summary>FK → Subscription (CASCADE).</summary>
    public int SubscriptionId { get; set; }

    /// <summary>FK → PlanAddOn (RESTRICT).</summary>
    public int AddOnId { get; set; }

    public int Quantity { get; set; } = 1;

    public DateTime ActivatedAt { get; set; }

    /// <summary>Soft deactivation; the row is kept for history. Null ⇒ active.</summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>Overrides <see cref="Catalogs.PlanAddOn.DefaultPriceCents"/> when set.</summary>
    public int? CustomPriceCents { get; set; }

    [MaxLength(64)]
    public string? ActivatedByTokenIdHash { get; set; }

    [MaxLength(300)]
    public string? Reason { get; set; }

    /// <summary>Stripe subscription-item id (<c>si_…</c>) on the Stripe rail.</summary>
    [MaxLength(64)]
    public string? StripeItemId { get; set; }

    /// <summary>
    /// The Stripe Price backing this add-on on the Stripe rail. Stored so a CUSTOM price
    /// (created at activation when <see cref="CustomPriceCents"/> is set) can be archived on
    /// deactivate — catalog prices are shared and never archived.
    /// </summary>
    [MaxLength(64)]
    public string? StripeAddOnPriceId { get; set; }

    /// <summary>
    /// The invoice this add-on was first pro-rated onto (mid-cycle activation). Plain
    /// nullable trace — set once by the generation job so the partial first-period charge
    /// is never emitted twice (same discipline as
    /// <c>SubscriptionPriceHistory.AppliedToInvoiceId</c>).
    /// </summary>
    public int? LastProRatedInvoiceId { get; set; }

    public Subscription? Subscription { get; set; }
    public PlanAddOn? PlanAddOn { get; set; }
}
