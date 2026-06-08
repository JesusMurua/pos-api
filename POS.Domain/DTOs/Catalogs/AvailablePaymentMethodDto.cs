using POS.Domain.Enums;

namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// A payment method available to the logged-in tenant (after plan matrix +
/// per-business override + country filtering). <see cref="Name"/> is the tenant's
/// custom label when set, otherwise the catalog name.
/// </summary>
public sealed record AvailablePaymentMethodDto(
    int Id,
    string Code,
    string Name,
    PaymentCategory Category,
    bool SupportsOverpay,
    bool RequiresReference,
    bool RequiresCustomer,
    string? ProviderKey,
    string? Icon,
    int SortOrder);
