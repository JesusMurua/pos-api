using POS.Domain.Models.Catalogs;

namespace POS.Services.IService;

/// <summary>
/// Provides read-only access to system catalogs for dropdowns, setup, and onboarding.
/// </summary>
public interface ICatalogService
{
    Task<IEnumerable<KitchenStatusCatalog>> GetKitchenStatusesAsync();
    Task<IEnumerable<DisplayStatusCatalog>> GetDisplayStatusesAsync();
    Task<IEnumerable<PaymentMethodCatalog>> GetPaymentMethodsAsync();
    Task<IEnumerable<DeviceModeCatalog>> GetDeviceModesAsync();
    Task<IEnumerable<BusinessTypeCatalog>> GetBusinessTypesAsync();
    Task<IEnumerable<MacroCategory>> GetMacroCategoriesAsync();
    Task<IEnumerable<ZoneTypeCatalog>> GetZoneTypesAsync();
    Task<IEnumerable<PlanTypeCatalog>> GetPlanTypesAsync();

    /// <summary>
    /// Returns the full Plan × Feature catalog as the single source of truth for
    /// which features each plan includes. Feature <c>Code</c> values match the
    /// <see cref="POS.Domain.Enums.FeatureKey"/> enum names verbatim, so the
    /// string is identical to what the JWT <c>features</c> claim carries.
    /// </summary>
    Task<IReadOnlyList<PlanCatalogDto>> GetPlanCatalogAsync();
}

/// <summary>
/// Public catalog entry for a single subscription plan.
/// </summary>
public class PlanCatalogDto
{
    public int Id { get; set; }
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int SortOrder { get; set; }

    /// <summary>
    /// Public monthly price in <see cref="Currency"/>. Null means the plan is
    /// not publicly priced (e.g. Enterprise → contact sales).
    /// </summary>
    public decimal? MonthlyPrice { get; set; }

    /// <summary>ISO 4217 currency code for <see cref="MonthlyPrice"/>.</summary>
    public string Currency { get; set; } = "MXN";

    public List<PlanCatalogFeatureDto> Features { get; set; } = new();
}

/// <summary>
/// One enabled feature inside a <see cref="PlanCatalogDto"/>.
/// </summary>
public class PlanCatalogFeatureDto
{
    /// <summary>
    /// Stable feature identifier. Exactly the <see cref="POS.Domain.Enums.FeatureKey"/>
    /// enum name (e.g. <c>"CustomerDatabase"</c>) so the frontend can compare this
    /// against the JWT <c>features</c> claim with a plain string match.
    /// </summary>
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsQuantitative { get; set; }
    public string? ResourceLabel { get; set; }

    /// <summary>Numeric cap for quantitative features. Null means unlimited.</summary>
    public int? DefaultLimit { get; set; }

    /// <summary>
    /// Macro categories where this feature is applicable. Empty when the feature
    /// is defined in the catalog but no macro currently exposes it.
    /// </summary>
    public List<int> ApplicableMacroCategoryIds { get; set; } = new();
}
