using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for generating sales reports.
/// </summary>
public interface IReportService
{
    /// <summary>
    /// Gets report summary data for a date range.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>Complete report summary with metrics, daily data, top products, and order details.</returns>
    Task<ReportSummary> GetSummaryAsync(int branchId, DateTime from, DateTime to);

    /// <summary>
    /// Generates Excel report as byte array.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>Excel file bytes.</returns>
    Task<byte[]> GenerateExcelAsync(int branchId, DateTime from, DateTime to);

    /// <summary>
    /// Generates PDF report as byte array.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>PDF file bytes.</returns>
    Task<byte[]> GeneratePdfAsync(int branchId, DateTime from, DateTime to);

    /// <summary>
    /// Generates a fiscal CSV export with order details including invoice status.
    /// Columns: OrderId, Date, Total, PaymentMethod, InvoiceStatus.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>CSV file bytes (UTF-8 with BOM).</returns>
    Task<byte[]> GenerateFiscalCsvAsync(int branchId, DateTime from, DateTime to);

    // ──────────────────────────────────────────
    // BDD-006: Advanced BI & Reporting
    // ──────────────────────────────────────────

    /// <summary>
    /// Returns aggregated chart data for the BI dashboard:
    /// time-series sales, top products, and payment-method breakdown.
    /// Only paid, non-cancelled orders are included.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="from">Start date (inclusive).</param>
    /// <param name="to">End date (inclusive).</param>
    /// <param name="granularity">Time bucket size: "hour", "day", or "month".</param>
    /// <returns>A <see cref="DashboardChartsDto"/> with all chart series.</returns>
    Task<DashboardChartsDto> GetDashboardChartsAsync(int branchId, DateTime from, DateTime to, string granularity);

    /// <summary>
    /// Generates a detailed sales CSV export for accounting or auditing.
    /// Columns: OrderId, Date, Total, PaymentMethods, CustomerName, Facturado.
    /// Only paid, non-cancelled orders are included.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="from">Start date (inclusive).</param>
    /// <param name="to">End date (inclusive).</param>
    /// <returns>CSV content as a UTF-8 string with BOM preamble (<c>\uFEFF</c>).</returns>
    Task<string> GetDetailedSalesCsvAsync(int branchId, DateTime from, DateTime to);
}
