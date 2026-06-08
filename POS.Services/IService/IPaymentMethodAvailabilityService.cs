using POS.Domain.DTOs.Catalogs;

namespace POS.Services.IService;

/// <summary>
/// Resolves the payment methods available to a tenant: catalog (active + country)
/// gated by the plan matrix and per-business override. Cached per tenant.
/// </summary>
public interface IPaymentMethodAvailabilityService
{
    Task<IReadOnlyList<AvailablePaymentMethodDto>> GetAvailableAsync(int businessId);
}
