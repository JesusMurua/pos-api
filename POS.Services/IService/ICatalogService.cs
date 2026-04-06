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
    Task<IEnumerable<ZoneTypeCatalog>> GetZoneTypesAsync();
    Task<IEnumerable<PlanTypeCatalog>> GetPlanTypesAsync();
}
