using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Per-business override of the plan payment-method matrix. A NEW axis (not the
/// Plan×BusinessType shape of features): it exists for one-off deals — a tenant
/// granted (or denied) a specific method regardless of its plan. When present,
/// its <see cref="IsEnabled"/> wins over the plan matrix.
/// </summary>
public class TenantPaymentMethodOverride
{
    public int Id { get; set; }

    public int BusinessId { get; set; }

    public int PaymentMethodId { get; set; }

    /// <summary>Final enablement for this (business, method) — overrides the plan.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>Optional tenant-facing label shown instead of the catalog name.</summary>
    [MaxLength(50)]
    public string? CustomLabel { get; set; }

    /// <summary>Opaque per-tenant provider configuration (jsonb); interpreted by the provider service.</summary>
    public string? ProviderConfigJson { get; set; }

    public Business? Business { get; set; }

    public PaymentMethodCatalog? PaymentMethod { get; set; }
}
