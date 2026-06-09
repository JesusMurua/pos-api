using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Data-driven catalog of the rails Fino uses to charge the TENANT the SaaS
/// subscription fee (tenant → operator) — distinct from <c>PaymentMethodCatalog</c>
/// (the POS rails the tenant uses to charge its own customers). Stripe is one rail
/// among N; manual rails (transfer/cash/cheque) are registered by the super-admin
/// when money is received. See docs/saas-billing-architecture.md §4.1 (source of truth).
/// </summary>
public class SaaSBillingMethod
{
    public int Id { get; set; }

    /// <summary>Stable freeze key (e.g. <c>Stripe</c>, <c>BankTransfer</c>). Unique.</summary>
    [Required, MaxLength(20)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(60)]
    public string Name { get; set; } = null!;

    /// <summary>true ⇒ the rail confirms payment via webhook (Stripe, OxxoPay);
    /// false ⇒ the operator registers the payment manually.</summary>
    public bool IsAutomatic { get; set; }

    /// <summary>true ⇒ a payment on this rail must carry a reference (bank folio, txn id).</summary>
    public bool RequiresReference { get; set; }

    /// <summary>External provider key (e.g. <c>stripe</c>); null for purely manual rails.</summary>
    [MaxLength(30)]
    public string? ProviderKey { get; set; }

    /// <summary>ISO country restriction (e.g. "MX"); null = all countries.</summary>
    [MaxLength(2)]
    public string? CountryCode { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Soft-delete flag; inactive rails are hidden from selectors.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>True for the rails seeded by the system — code-owned, never hard-deletable.</summary>
    public bool IsSystem { get; set; }
}
