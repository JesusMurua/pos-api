using POS.Domain.DTOs.Admin;

namespace POS.Services.IService;

/// <summary>
/// Aggregates the super-admin financial dashboard over the existing billing data (PR-6). No new
/// entities — pure read aggregation. MRR/ARR are a current snapshot; the time series is collected
/// revenue (not historical MRR); retention is reconstructed from CanceledAt. All MXN (OQ-10).
/// </summary>
public interface IBillingMetricsService
{
    Task<AdminBillingMetricsDto> GetMetricsAsync(int lookbackMonths = 12, CancellationToken ct = default);

    /// <summary>Upcoming MANUAL-rail invoices in the next <paramref name="days"/> days (Stripe rail is
    /// authoritative in the Stripe Dashboard and excluded here).</summary>
    Task<IReadOnlyList<UpcomingInvoiceDto>> GetUpcomingInvoicesAsync(int days = 30, CancellationToken ct = default);
}
