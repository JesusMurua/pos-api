using POS.Domain.Models.Catalogs;

namespace POS.Repository.IRepository;

public interface ICatalogRepository
{
    Task<IEnumerable<PlanTypeCatalog>> GetPlanTypesAsync();
    Task<IEnumerable<BusinessTypeCatalog>> GetBusinessTypesAsync();
    Task<IEnumerable<ZoneTypeCatalog>> GetZoneTypesAsync();
    Task<IEnumerable<UserRoleCatalog>> GetUserRolesAsync();
    Task<IEnumerable<PaymentMethodCatalog>> GetPaymentMethodsAsync();
    Task<IEnumerable<KitchenStatusCatalog>> GetKitchenStatusesAsync();
    Task<IEnumerable<DisplayStatusCatalog>> GetDisplayStatusesAsync();
    Task<IEnumerable<DeviceModeCatalog>> GetDeviceModesAsync();
    Task<IEnumerable<PromotionTypeCatalog>> GetPromotionTypesAsync();
    Task<IEnumerable<PromotionScopeCatalog>> GetPromotionScopesAsync();
    Task<IEnumerable<OrderSyncStatusCatalog>> GetOrderSyncStatusesAsync();
}
