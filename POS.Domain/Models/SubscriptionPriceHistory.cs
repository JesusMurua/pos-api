using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

/// <summary>
/// Append-only log of negotiated price changes on a <see cref="Subscription"/>.
/// One row per <c>BaseAmountCents</c> change, with before/after, who/why and when
/// it takes effect. Written by the admin subscription surface in PR-2 (no writer in
/// PR-1b — this is foundation). See docs/saas-billing-architecture.md §4.6.
/// </summary>
public class SubscriptionPriceHistory
{
    public int Id { get; set; }

    /// <summary>FK → Subscription (RESTRICT).</summary>
    public int SubscriptionId { get; set; }

    public int BeforeAmountCents { get; set; }

    public int AfterAmountCents { get; set; }

    public DateTime ChangedAtUtc { get; set; }

    /// <summary>Hashed admin token id (the <c>token_id</c> claim) that made the change.</summary>
    [MaxLength(64)]
    public string? ChangedByTokenId { get; set; }

    /// <summary>Commercial reason (discount, negotiated, free month…).</summary>
    [Required, MaxLength(300)]
    public string Reason { get; set; } = null!;

    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// The invoice this price change was first applied to. Plain nullable column in
    /// PR-1b — the FK → SubscriptionInvoice is added in PR-3 when that table exists.
    /// </summary>
    public int? AppliedToInvoiceId { get; set; }

    public Subscription? Subscription { get; set; }
}
