using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Data-driven catalog of payment methods. The <see cref="Category"/> drives
/// behavior/reporting; the concrete rows are super-admin editable (PR-B).
/// Seed values are reconciled in DbInitializer — see
/// docs/payment-method-catalog-architecture.md §4.1 (source of truth).
/// </summary>
public class PaymentMethodCatalog
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Name { get; set; } = null!;

    public int SortOrder { get; set; }

    /// <summary>Behavioral category driving gating and report buckets.</summary>
    public PaymentCategory Category { get; set; } = PaymentCategory.Other;

    /// <summary>SAT CFDI 4.0 "Forma de Pago" code (e.g. "01" cash, "04" card).</summary>
    [Required, MaxLength(2)]
    public string SatPaymentFormCode { get; set; } = "99";

    /// <summary>Requires a reference/folio to be recorded (declarative gating).</summary>
    public bool RequiresReference { get; set; }

    /// <summary>Requires a customer (store credit / loyalty points).</summary>
    public bool RequiresCustomer { get; set; }

    /// <summary>Whether overpayment (and therefore change) is allowed. Cash only.</summary>
    public bool SupportsOverpay { get; set; }

    /// <summary>Whether the method can cover part of an order in a split payment.</summary>
    public bool SupportsPartial { get; set; } = true;

    /// <summary>External provider key (e.g. "clip", "mercadopago"); null for manual.</summary>
    [MaxLength(30)]
    public string? ProviderKey { get; set; }

    /// <summary>ISO country restriction (e.g. "MX"); null = global.</summary>
    [MaxLength(2)]
    public string? CountryCode { get; set; }

    /// <summary>Icon class hint for the UI (e.g. "pi-money-bill").</summary>
    [MaxLength(40)]
    public string? IconClass { get; set; }

    /// <summary>Soft-delete flag; inactive methods are hidden from selectors.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>True for the base methods seeded by the system — never hard-deletable.</summary>
    public bool IsSystem { get; set; }
}
