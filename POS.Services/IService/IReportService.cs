using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for generating sales reports. All date-range methods
/// interpret bounds as inclusive local calendar days in the branch's timezone.
/// The implementation performs UTC-range math internally.
/// </summary>
public interface IReportService
{
    /// <summary>
    /// Gets report summary data for a local calendar date range <c>[from, to]</c> inclusive.
    /// </summary>
    Task<ReportSummary> GetSummaryAsync(int branchId, DateOnly from, DateOnly to);

    /// <summary>
    /// Generates Excel report as byte array.
    /// </summary>
    Task<byte[]> GenerateExcelAsync(int branchId, DateOnly from, DateOnly to);

    /// <summary>
    /// Generates PDF report as byte array.
    /// </summary>
    Task<byte[]> GeneratePdfAsync(int branchId, DateOnly from, DateOnly to);

    /// <summary>
    /// Generates a fiscal CSV export with order details including invoice status.
    /// Columns: OrderId, Date, Total, PaymentMethod, InvoiceStatus.
    /// </summary>
    Task<byte[]> GenerateFiscalCsvAsync(int branchId, DateOnly from, DateOnly to);

    // ──────────────────────────────────────────
    // BDD-006: Advanced BI & Reporting
    // ──────────────────────────────────────────

    /// <summary>
    /// Returns aggregated chart data for the BI dashboard:
    /// time-series sales, top products, and payment-method breakdown.
    /// Only paid, non-cancelled orders are included.
    /// </summary>
    /// <param name="granularity">Time bucket size: "hour", "day", or "month".</param>
    Task<DashboardChartsDto> GetDashboardChartsAsync(int branchId, DateOnly from, DateOnly to, string granularity);

    /// <summary>
    /// Generates a detailed sales CSV export for accounting or auditing.
    /// Columns: OrderId, Date, Total, PaymentMethods, CustomerName, Facturado.
    /// Only paid, non-cancelled orders are included.
    /// </summary>
    /// <returns>CSV content as a UTF-8 string with BOM preamble (<c>\uFEFF</c>).</returns>
    Task<string> GetDetailedSalesCsvAsync(int branchId, DateOnly from, DateOnly to);
}
