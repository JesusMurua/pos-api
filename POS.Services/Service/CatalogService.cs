using Microsoft.Extensions.Caching.Memory;
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
    private const string PlanCatalogCacheKey = "CatalogService::PlanCatalog";
    private static readonly TimeSpan PlanCatalogCacheTtl = TimeSpan.FromMinutes(30);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;

    public CatalogService(IUnitOfWork unitOfWork, IMemoryCache cache)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
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

    public Task<IReadOnlyList<PlanCatalogDto>> GetPlanCatalogAsync()
    {
        return _cache.GetOrCreateAsync(PlanCatalogCacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PlanCatalogCacheTtl;
            return await BuildPlanCatalogAsync();
        })!;
    }

    private async Task<IReadOnlyList<PlanCatalogDto>> BuildPlanCatalogAsync()
    {
        var plans = (await _unitOfWork.Catalog.GetPlanTypesAsync()).ToList();
        var features = (await _unitOfWork.Catalog.GetFeatureCatalogsAsync()).ToList();
        var matrix = (await _unitOfWork.Catalog.GetPlanFeatureMatricesAsync()).ToList();
        var applicability = (await _unitOfWork.Catalog.GetBusinessTypeFeaturesAsync()).ToList();

        var featuresById = features.ToDictionary(f => f.Id);

        // Group macro applicability once so per-plan resolution is O(1) per feature.
        var applicableMacrosByFeatureId = applicability
            .GroupBy(a => a.FeatureId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(a => a.MacroCategoryId).OrderBy(id => id).ToList());

        var matrixByPlan = matrix.GroupBy(m => m.PlanTypeId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<PlanCatalogDto>(plans.Count);

        foreach (var plan in plans.OrderBy(p => p.SortOrder))
        {
            var planDto = new PlanCatalogDto
            {
                Id = plan.Id,
                Code = plan.Code,
                Name = plan.Name,
                SortOrder = plan.SortOrder,
                MonthlyPrice = plan.MonthlyPrice,
                Currency = plan.Currency
            };

            if (matrixByPlan.TryGetValue(plan.Id, out var planRows))
            {
                foreach (var row in planRows.Where(r => r.IsEnabled))
                {
                    if (!featuresById.TryGetValue(row.FeatureId, out var feature))
                        continue;

                    // Emit the enum name directly so the string is identical to the
                    // one the JWT `features` claim carries (see FeatureGateService).
                    var code = feature.Key.ToString();

                    planDto.Features.Add(new PlanCatalogFeatureDto
                    {
                        Code = code,
                        Name = feature.Name,
                        Description = feature.Description,
                        IsQuantitative = feature.IsQuantitative,
                        ResourceLabel = feature.ResourceLabel,
                        DefaultLimit = row.DefaultLimit,
                        ApplicableMacroCategoryIds = applicableMacrosByFeatureId.TryGetValue(feature.Id, out var macros)
                            ? macros
                            : new List<int>()
                    });
                }

                planDto.Features = planDto.Features
                    .OrderBy(f => f.Code, StringComparer.Ordinal)
                    .ToList();
            }

            result.Add(planDto);
        }

        return result;
    }
}
