using POS.Domain.Models.Catalogs;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Read-only service for system catalogs. Thin pass-through to the repository
/// that keeps the controller free of IUnitOfWork.
/// </summary>
public class CatalogService : ICatalogService
{
    private readonly IUnitOfWork _unitOfWork;

    public CatalogService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<IEnumerable<KitchenStatusCatalog>> GetKitchenStatusesAsync() =>
        await _unitOfWork.Catalog.GetKitchenStatusesAsync();

    public async Task<IEnumerable<DisplayStatusCatalog>> GetDisplayStatusesAsync() =>
        await _unitOfWork.Catalog.GetDisplayStatusesAsync();

    public async Task<IEnumerable<PaymentMethodCatalog>> GetPaymentMethodsAsync() =>
        await _unitOfWork.Catalog.GetPaymentMethodsAsync();

    public async Task<IEnumerable<DeviceModeCatalog>> GetDeviceModesAsync() =>
        await _unitOfWork.Catalog.GetDeviceModesAsync();

    public async Task<IEnumerable<BusinessTypeCatalog>> GetBusinessTypesAsync() =>
        await _unitOfWork.Catalog.GetBusinessTypesAsync();

    public async Task<IEnumerable<MacroCategory>> GetMacroCategoriesAsync() =>
        await _unitOfWork.Catalog.GetMacroCategoriesAsync();

    public async Task<IEnumerable<ZoneTypeCatalog>> GetZoneTypesAsync() =>
        await _unitOfWork.Catalog.GetZoneTypesAsync();

    public async Task<IEnumerable<PlanTypeCatalog>> GetPlanTypesAsync() =>
        await _unitOfWork.Catalog.GetPlanTypesAsync();
}
