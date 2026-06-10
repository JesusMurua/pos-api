using POS.Domain.Models.Catalogs;

namespace POS.Repository.IRepository;

public interface ICatalogRepository
{
    Task<IEnumerable<PlanTypeCatalog>> GetPlanTypesAsync();
    Task<IEnumerable<BusinessTypeCatalog>> GetBusinessTypesAsync();
    Task<IEnumerable<MacroCategory>> GetMacroCategoriesAsync();
    Task<IEnumerable<ZoneTypeCatalog>> GetZoneTypesAsync();
    Task<IEnumerable<UserRoleCatalog>> GetUserRolesAsync();
    Task<IEnumerable<PaymentMethodCatalog>> GetPaymentMethodsAsync();
    Task<IEnumerable<PlanPaymentMethodMatrix>> GetPlanPaymentMethodMatricesAsync();
    Task<IEnumerable<TenantPaymentMethodOverride>> GetTenantPaymentMethodOverridesAsync(int businessId);
    Task<IEnumerable<KitchenStatusCatalog>> GetKitchenStatusesAsync();
    Task<IEnumerable<DisplayStatusCatalog>> GetDisplayStatusesAsync();
    Task<IEnumerable<DeviceModeCatalog>> GetDeviceModesAsync();
    Task<IEnumerable<PromotionTypeCatalog>> GetPromotionTypesAsync();
    Task<IEnumerable<PromotionScopeCatalog>> GetPromotionScopesAsync();
    Task<IEnumerable<OrderSyncStatusCatalog>> GetOrderSyncStatusesAsync();

    Task<IEnumerable<FeatureCatalog>> GetFeatureCatalogsAsync();
    Task<IEnumerable<PlanFeatureMatrix>> GetPlanFeatureMatricesAsync();
    Task<IEnumerable<BusinessTypeFeature>> GetBusinessTypeFeaturesAsync();

    Task<IEnumerable<AccessReasonCatalog>> GetAccessReasonsAsync();
    Task<IEnumerable<AccessMethodCatalog>> GetAccessMethodsAsync();

    /// <summary>Forward lookup of the catalog Stripe price id for self-service checkout (PR-2).</summary>
    Task<string?> GetStripePlanPriceIdAsync(int planTypeId, string billingCycle, string pricingGroup);
}
