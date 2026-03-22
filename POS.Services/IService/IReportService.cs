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
}
