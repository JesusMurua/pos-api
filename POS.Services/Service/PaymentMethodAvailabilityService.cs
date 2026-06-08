using Microsoft.Extensions.Caching.Memory;
using POS.Domain.DTOs.Catalogs;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class PaymentMethodAvailabilityService : IPaymentMethodAvailabilityService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;
    private readonly PaymentMethodCacheGeneration _generation;
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    public PaymentMethodAvailabilityService(
        IUnitOfWork unitOfWork, IMemoryCache cache, PaymentMethodCacheGeneration generation)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _generation = generation;
    }

    public async Task<IReadOnlyList<AvailablePaymentMethodDto>> GetAvailableAsync(int businessId)
    {
        // Generation-versioned key: an admin mutation bumps the generation and
        // orphans every tenant's entry at once. Plan upgrades reflect within the TTL.
        var key = $"PaymentMethodsAvailable::{_generation.Current}::{businessId}";
        if (_cache.TryGetValue(key, out IReadOnlyList<AvailablePaymentMethodDto>? cached) && cached != null)
            return cached;

        var result = await ComputeAsync(businessId);
        _cache.Set(key, result, Ttl);
        return result;
    }

    private async Task<IReadOnlyList<AvailablePaymentMethodDto>> ComputeAsync(int businessId)
    {
        var business = await _unitOfWork.Business.GetByIdAsync(businessId);
        if (business == null) return Array.Empty<AvailablePaymentMethodDto>();

        var methods = (await _unitOfWork.Catalog.GetPaymentMethodsAsync()).ToList();
        var matrix = (await _unitOfWork.Catalog.GetPlanPaymentMethodMatricesAsync()).ToList();
        var anyMatrixRows = matrix.Count > 0;
        var planEnabled = matrix
            .Where(m => m.PlanTypeId == business.PlanTypeId)
            .ToDictionary(m => m.PaymentMethodId, m => m.IsEnabled);
        var overrides = (await _unitOfWork.Catalog.GetTenantPaymentMethodOverridesAsync(businessId))
            .ToDictionary(o => o.PaymentMethodId, o => o);

        var available = new List<AvailablePaymentMethodDto>();
        foreach (var m in methods.OrderBy(x => x.SortOrder).ThenBy(x => x.Code))
        {
            if (!m.IsActive) continue;
            if (m.CountryCode != null && m.CountryCode != business.CountryCode) continue;

            bool authorized;
            var name = m.Name;
            if (overrides.TryGetValue(m.Id, out var ov))
            {
                authorized = ov.IsEnabled;                     // override wins
                if (!string.IsNullOrWhiteSpace(ov.CustomLabel)) name = ov.CustomLabel!;
            }
            else if (!anyMatrixRows)
            {
                authorized = true;                             // matrix not seeded yet (transition)
            }
            else
            {
                authorized = planEnabled.TryGetValue(m.Id, out var en) && en;
            }

            if (!authorized) continue;

            available.Add(new AvailablePaymentMethodDto(
                m.Id, m.Code, name, m.Category, m.SupportsOverpay,
                m.RequiresReference, m.RequiresCustomer, m.ProviderKey, m.IconClass, m.SortOrder));
        }

        return available;
    }
}
