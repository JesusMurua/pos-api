using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

/// <summary>
/// A SaaS invoice the operator (Fino) issues to a tenant (tenant → operator billing).
/// <b>Not</b> <see cref="Invoice"/> — that is the tenant's CFDI to its own customer.
///
/// Two production paths (docs/saas-billing-architecture.md §2, model C):
/// <list type="bullet">
/// <item>Manual rails: <c>IInvoiceGenerationService</c> generates these locally each
///   cycle (Local SSoT) and the backend computes IVA.</item>
/// <item>Stripe rail: the worker mirrors Stripe's invoice from the
///   <c>invoice.payment_succeeded</c> webhook (Stripe SSoT), copying Stripe's amounts —
///   it never re-computes tax.</item>
/// </list>
/// The CFDI receptor block is frozen from <see cref="Business"/> at issue time (M1) when
/// the subscription opted into CFDI; the SAT stamping fields are populated in PR-7.
/// </summary>
public class SubscriptionInvoice
{
    public int Id { get; set; }

    /// <summary>FK → Subscription (RESTRICT).</summary>
    public int SubscriptionId { get; set; }

    /// <summary>Denormalized owning business (G3). Indexed.</summary>
    public int BusinessId { get; set; }

    /// <summary>Per-business sequence from <see cref="Business.InvoiceCounter"/> (M3). Unique with BusinessId.</summary>
    public int InvoiceNumber { get; set; }

    public SubscriptionInvoiceStatus Status { get; set; } = SubscriptionInvoiceStatus.Open;

    public DateTime IssuedAtUtc { get; set; }
    public DateTime DueDate { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }

    public int SubtotalCents { get; set; }
    public int TaxCents { get; set; }
    public int TotalCents { get; set; }

    /// <summary>ISO 4217. "MXN" today (multi-currency is OQ-10).</summary>
    [MaxLength(3)]
    public string Currency { get; set; } = "MXN";

    /// <summary>Hashed admin token id; null ⇒ created by the generation job / webhook.</summary>
    [MaxLength(64)]
    public string? CreatedByTokenIdHash { get; set; }

    /// <summary>Set when the Stripe rail produced this invoice (Stripe SSoT mirror). Unique when present.</summary>
    [MaxLength(64)]
    public string? StripeInvoiceId { get; set; }

    // ── CFDI receptor (frozen from Business at issue, all nullable until CfdiRequired + PR-7) ──

    /// <summary>Frozen from <see cref="Business.Rfc"/> at issue.</summary>
    [MaxLength(13)]
    public string? ReceptorRfc { get; set; }

    /// <summary>Frozen from <see cref="Business.TaxRegime"/>.</summary>
    [MaxLength(3)]
    public string? ReceptorRegime { get; set; }

    /// <summary>Frozen from <see cref="Business.LegalName"/>.</summary>
    [MaxLength(300)]
    public string? ReceptorLegalName { get; set; }

    /// <summary>Receptor postal code (CFDI 4.0).</summary>
    [MaxLength(5)]
    public string? ReceptorPostalCode { get; set; }

    /// <summary>CFDI use code (e.g. G03).</summary>
    [MaxLength(4)]
    public string? CfdiUseCode { get; set; }

    /// <summary>Frozen from the paying rail (SAT forma de pago).</summary>
    [MaxLength(2)]
    public string? SatPaymentFormCode { get; set; }

    /// <summary>Folio fiscal once stamped (PR-7).</summary>
    [MaxLength(40)]
    public string? SatUuid { get; set; }

    public DateTime? SatStampedAt { get; set; }

    [MaxLength(500)]
    public string? SatXmlUrl { get; set; }

    [MaxLength(500)]
    public string? SatPdfUrl { get; set; }

    public Subscription? Subscription { get; set; }
    public ICollection<SubscriptionInvoiceItem> Items { get; set; } = new List<SubscriptionInvoiceItem>();
    public ICollection<TenantPayment> Payments { get; set; } = new List<TenantPayment>();
}
