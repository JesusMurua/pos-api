using POS.Domain.DTOs.Admin;

namespace POS.Services.IService;

/// <summary>
/// Operator surface over SaaS <c>SubscriptionInvoice</c>s (manual rails). Creation,
/// edit-while-Open, and void. Payments are handled by <see cref="IAdminTenantPaymentService"/>.
/// </summary>
public interface IAdminInvoiceService
{
    Task<IReadOnlyList<AdminInvoiceListItemDto>> GetForBusinessAsync(int businessId);

    Task<AdminInvoiceDetailDto> GetAsync(int invoiceId);

    /// <summary>Creates a manual invoice (assigns InvoiceNumber, computes IVA, opens it).</summary>
    Task<AdminInvoiceDetailDto> CreateAsync(int businessId, AdminCreateInvoiceRequest request, string? tokenId);

    /// <summary>Edits an invoice while it is still Open. 400 otherwise.</summary>
    Task UpdateAsync(int invoiceId, AdminUpdateInvoiceRequest request, string? tokenId);

    /// <summary>
    /// Voids an invoice. Allowed only from {Open, Overdue} — a PartiallyPaid invoice has
    /// recorded money and must have its payments deleted first (refunds are deferred, OQ-9).
    /// </summary>
    Task VoidAsync(int invoiceId, string? reason, string? tokenId);
}
