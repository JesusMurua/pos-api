using POS.Domain.DTOs.Catalogs;
using POS.Domain.DTOs.Tax;

namespace POS.Services.IService;

/// <summary>
/// Read-only service that surfaces system catalogs to the public API.
/// Every method returns a <see cref="CatalogResponse{T}"/> envelope
/// carrying the projected DTO list and the strong ETag computed by the
/// cache-aside layer (see BDD-021 §6.1).
/// <para>
/// Internal consumers needing entity-level access MUST query the
/// repository (<c>IUnitOfWork.Catalog.GetXxxAsync</c>) directly — this
/// service is the public-facing DTO projector, not a general-purpose
/// catalog gateway (BDD-021 FR-009 / EC-4).
/// </para>
/// </summary>
public interface ICatalogService
{
    /// <summary>Returns the catalog of kitchen statuses.</summary>
    Task<CatalogResponse<KitchenStatusDto>> GetKitchenStatusesAsync();

    /// <summary>Returns the catalog of display statuses.</summary>
    Task<CatalogResponse<DisplayStatusDto>> GetDisplayStatusesAsync();

    /// <summary>Returns the catalog of payment methods.</summary>
    Task<CatalogResponse<PaymentMethodDto>> GetPaymentMethodsAsync();

    /// <summary>Returns the catalog of device modes.</summary>
    Task<CatalogResponse<DeviceModeDto>> GetDeviceModesAsync();

    /// <summary>Returns the catalog of business sub-giros (types).</summary>
    Task<CatalogResponse<BusinessTypeDto>> GetBusinessTypesAsync();

    /// <summary>Returns the catalog of macro categories.</summary>
    Task<CatalogResponse<MacroCategoryDto>> GetMacroCategoriesAsync();

    /// <summary>Returns the catalog of zone types.</summary>
    Task<CatalogResponse<ZoneTypeDto>> GetZoneTypesAsync();

    /// <summary>Returns the catalog of subscription plan types (flat list).</summary>
    Task<CatalogResponse<PlanTypeDto>> GetPlanTypesAsync();

    /// <summary>Returns the catalog of access reasons (gym / wellness access control).</summary>
    Task<CatalogResponse<AccessReasonDto>> GetAccessReasonsAsync();

    /// <summary>Returns the catalog of access methods (gym / wellness access control).</summary>
    Task<CatalogResponse<AccessMethodDto>> GetAccessMethodsAsync();

    /// <summary>
    /// Returns the full Plan × Feature catalog. Single source of truth for
    /// which features each plan enables. Feature <c>Code</c> values match
    /// the <see cref="POS.Domain.Enums.FeatureKey"/> enum names verbatim,
    /// identical to the JWT <c>features</c> claim emitted by the auth flow.
    /// </summary>
    Task<CatalogResponse<PlanCatalogDto>> GetPlanCatalogAsync();

    /// <summary>
    /// Returns the catalog of tax rows. When <paramref name="countryCode"/>
    /// is provided (ISO 3166-1 alpha-2), results are filtered to that
    /// country; otherwise every tax row is returned. Unknown country codes
    /// yield an empty payload (200), not 404.
    /// </summary>
    /// <param name="countryCode">
    /// Optional ISO 3166-1 alpha-2 filter. When supplied, must be exactly
    /// 2 alphabetic characters (validated upstream by the controller — see
    /// VR-001 in BDD-021 §6.2).
    /// </param>
    Task<CatalogResponse<TaxDto>> GetTaxCatalogAsync(string? countryCode = null);

    /// <summary>
    /// Invalidates the cached envelope for a specific catalog resource (when
    /// <paramref name="resourceName"/> is supplied) or every cached catalog
    /// (when null). Used by future admin-triggered cache rebuilds.
    /// Not exposed over HTTP in v1 — see BDD-021 §5.2 / §7.3.
    /// </summary>
    /// <param name="resourceName">
    /// Cache resource name in PascalCase (e.g. <c>MacroCategories</c>,
    /// <c>BusinessTypes</c>). Null clears every catalog cache key.
    /// </param>
    void Invalidate(string? resourceName = null);
}
