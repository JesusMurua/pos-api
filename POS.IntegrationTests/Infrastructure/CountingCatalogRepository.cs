using POS.Domain.Models.Catalogs;
using POS.Repository.IRepository;

namespace POS.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only decorator over <see cref="ICatalogRepository"/> that
/// increments <see cref="EFQueryCounterInterceptor.Count"/> on every
/// method invocation it forwards to the inner repository.
/// <para>
/// Implements the D1 fallback documented in BDD-021 §9 / Appendix C: the
/// EF Core <c>IMaterializationInterceptor</c> approach does not fire on
/// the InMemory provider, so cache miss / hit measurement is moved to the
/// repository boundary where the call signal is provider-agnostic.
/// </para>
/// </summary>
public class CountingCatalogRepository : ICatalogRepository
{
    private readonly ICatalogRepository _inner;
    private readonly EFQueryCounterInterceptor _counter;

    public CountingCatalogRepository(ICatalogRepository inner, EFQueryCounterInterceptor counter)
    {
        _inner = inner;
        _counter = counter;
    }

    public Task<IEnumerable<PlanTypeCatalog>> GetPlanTypesAsync()
    {
        _counter.Increment();
        return _inner.GetPlanTypesAsync();
    }

    public Task<IEnumerable<BusinessTypeCatalog>> GetBusinessTypesAsync()
    {
        _counter.Increment();
        return _inner.GetBusinessTypesAsync();
    }

    public Task<IEnumerable<MacroCategory>> GetMacroCategoriesAsync()
    {
        _counter.Increment();
        return _inner.GetMacroCategoriesAsync();
    }

    public Task<IEnumerable<ZoneTypeCatalog>> GetZoneTypesAsync()
    {
        _counter.Increment();
        return _inner.GetZoneTypesAsync();
    }

    public Task<IEnumerable<UserRoleCatalog>> GetUserRolesAsync()
    {
        _counter.Increment();
        return _inner.GetUserRolesAsync();
    }

    public Task<IEnumerable<PaymentMethodCatalog>> GetPaymentMethodsAsync()
    {
        _counter.Increment();
        return _inner.GetPaymentMethodsAsync();
    }

    public Task<IEnumerable<PlanPaymentMethodMatrix>> GetPlanPaymentMethodMatricesAsync()
    {
        _counter.Increment();
        return _inner.GetPlanPaymentMethodMatricesAsync();
    }

    public Task<IEnumerable<TenantPaymentMethodOverride>> GetTenantPaymentMethodOverridesAsync(int businessId)
    {
        _counter.Increment();
        return _inner.GetTenantPaymentMethodOverridesAsync(businessId);
    }

    public Task<IEnumerable<KitchenStatusCatalog>> GetKitchenStatusesAsync()
    {
        _counter.Increment();
        return _inner.GetKitchenStatusesAsync();
    }

    public Task<IEnumerable<DisplayStatusCatalog>> GetDisplayStatusesAsync()
    {
        _counter.Increment();
        return _inner.GetDisplayStatusesAsync();
    }

    public Task<IEnumerable<DeviceModeCatalog>> GetDeviceModesAsync()
    {
        _counter.Increment();
        return _inner.GetDeviceModesAsync();
    }

    public Task<IEnumerable<PromotionTypeCatalog>> GetPromotionTypesAsync()
    {
        _counter.Increment();
        return _inner.GetPromotionTypesAsync();
    }

    public Task<IEnumerable<PromotionScopeCatalog>> GetPromotionScopesAsync()
    {
        _counter.Increment();
        return _inner.GetPromotionScopesAsync();
    }

    public Task<IEnumerable<OrderSyncStatusCatalog>> GetOrderSyncStatusesAsync()
    {
        _counter.Increment();
        return _inner.GetOrderSyncStatusesAsync();
    }

    public Task<IEnumerable<FeatureCatalog>> GetFeatureCatalogsAsync()
    {
        _counter.Increment();
        return _inner.GetFeatureCatalogsAsync();
    }

    public Task<IEnumerable<PlanFeatureMatrix>> GetPlanFeatureMatricesAsync()
    {
        _counter.Increment();
        return _inner.GetPlanFeatureMatricesAsync();
    }

    public Task<IEnumerable<BusinessTypeFeature>> GetBusinessTypeFeaturesAsync()
    {
        _counter.Increment();
        return _inner.GetBusinessTypeFeaturesAsync();
    }

    public Task<IEnumerable<AccessReasonCatalog>> GetAccessReasonsAsync()
    {
        _counter.Increment();
        return _inner.GetAccessReasonsAsync();
    }

    public Task<IEnumerable<AccessMethodCatalog>> GetAccessMethodsAsync()
    {
        _counter.Increment();
        return _inner.GetAccessMethodsAsync();
    }

    public Task<string?> GetStripePlanPriceIdAsync(int planTypeId, string billingCycle, string pricingGroup)
    {
        _counter.Increment();
        return _inner.GetStripePlanPriceIdAsync(planTypeId, billingCycle, pricingGroup);
    }
}
