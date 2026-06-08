using POS.Domain.DTOs.Admin;

namespace POS.Services.IService;

/// <summary>
/// Ops-only management of the payment-method catalog, plan matrix and tenant
/// overrides. Every mutation is audited (by admin token) and invalidates the
/// payment caches (public /available + the anonymous catalog envelope).
/// </summary>
public interface IPaymentMatrixAdminService
{
    Task<IReadOnlyList<PaymentMethodCatalogDto>> GetCatalogAsync();
    Task<PaymentMethodCatalogDto> CreateCatalogAsync(UpsertPaymentMethodCatalogRequest request, string? tokenId);
    Task UpdateCatalogAsync(int id, UpsertPaymentMethodCatalogRequest request, string? tokenId);
    /// <summary>Soft-deletes (IsActive=false) when the method has payments; hard-deletes when none. Rejects system methods.</summary>
    Task DeleteCatalogAsync(int id, string? tokenId);

    Task<IReadOnlyList<PlanPaymentMethodEntryDto>> GetPlanMatrixAsync();
    Task BulkUpsertPlanMatrixAsync(IReadOnlyList<PlanPaymentMethodEntryDto> entries, string? tokenId);

    Task<IReadOnlyList<TenantPaymentMethodOverrideDto>> GetOverridesAsync();
    Task<TenantPaymentMethodOverrideDto> CreateOverrideAsync(CreateTenantOverrideRequest request, string? tokenId);
    Task UpdateOverrideAsync(int id, UpdateTenantOverrideRequest request, string? tokenId);
    Task DeleteOverrideAsync(int id, string? tokenId);

    Task<PaymentPreviewImpactDto> PreviewImpactAsync(int paymentMethodId, int planTypeId, bool enabled);
    Task<PagedPaymentAuditLogDto> GetAuditLogAsync(DateTime? from, DateTime? to, string? axis, int page, int pageSize);
}
