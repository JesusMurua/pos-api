using POS.Domain.DTOs.Admin;

namespace POS.Services.IService;

/// <summary>
/// Read-only admin (X-Admin-Token) access to the <c>BusinessAuditLog</c> trail
/// (the explicit operator-action log written since PR-1a). Paged + filterable.
/// Cross-tenant by design — uses <c>IgnoreQueryFilters</c>. See docs/saas-billing-api.md §7.
/// </summary>
public interface IAdminAuditLogService
{
    /// <summary>Audit rows for one tenant, newest first. <paramref name="action"/> is a
    /// <c>BusinessAuditAction</c> name; an unknown name yields zero rows (no silent wildcard).</summary>
    Task<PagedBusinessAuditLogDto> GetForBusinessAsync(
        int businessId, string? action, DateTime? from, DateTime? to, int page, int pageSize);

    /// <summary>Cross-tenant audit rows, newest first. <paramref name="businessId"/> optionally narrows to one tenant.</summary>
    Task<PagedBusinessAuditLogDto> GetCrossTenantAsync(
        int? businessId, string? action, DateTime? from, DateTime? to, int page, int pageSize);
}
