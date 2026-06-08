namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Junction between PlanTypeCatalog and PaymentMethodCatalog: which payment
/// methods each plan tier includes. Analogous to <see cref="PlanFeatureMatrix"/>.
/// Seeded with every (plan × method) combination and an explicit
/// <see cref="IsEnabled"/>, so absence of a row only happens pre-seed.
/// </summary>
public class PlanPaymentMethodMatrix
{
    public int PlanTypeId { get; set; }

    public int PaymentMethodId { get; set; }

    /// <summary>True if the plan includes the payment method.</summary>
    public bool IsEnabled { get; set; }

    public PlanTypeCatalog? PlanTypeCatalog { get; set; }

    public PaymentMethodCatalog? PaymentMethod { get; set; }
}
