using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

/// <summary>
/// Money received from a tenant against a <see cref="SubscriptionInvoice"/>, per rail.
/// Automatic (Stripe webhook, <c>ReceivedByTokenIdHash = null</c>) or manual super-admin
/// entry (hash set). Idempotency (M4): partial unique <c>(BillingMethodId, Reference)</c>
/// where Reference is present — rails without a reference (cash) cannot be deduped and the
/// operator owns correctness (<c>DELETE …/payments/{id}</c> fixes capture errors).
/// </summary>
public class TenantPayment
{
    public int Id { get; set; }

    /// <summary>FK → SubscriptionInvoice (RESTRICT).</summary>
    public int InvoiceId { get; set; }

    /// <summary>FK → SaaSBillingMethod (RESTRICT) — the rail the money arrived on.</summary>
    public int BillingMethodId { get; set; }

    public int AmountCents { get; set; }

    [MaxLength(3)]
    public string Currency { get; set; } = "MXN";

    public DateTime PaidAtUtc { get; set; }

    /// <summary>Bank folio / Stripe charge or invoice id. Drives idempotency when present.</summary>
    [MaxLength(120)]
    public string? Reference { get; set; }

    [MaxLength(300)]
    public string? Notes { get; set; }

    /// <summary>Hashed admin token id; null ⇒ automatic (webhook), hash ⇒ manual entry.</summary>
    [MaxLength(64)]
    public string? ReceivedByTokenIdHash { get; set; }

    [MaxLength(64)]
    public string? StripeChargeId { get; set; }

    /// <summary>Audit of the source webhook event.</summary>
    public string? RawWebhookPayloadJson { get; set; }

    public SubscriptionInvoice? Invoice { get; set; }
    public Catalogs.SaaSBillingMethod? BillingMethod { get; set; }
}
