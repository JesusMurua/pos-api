using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.API.Filters;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for sales reports and export.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class ReportController : BaseApiController
{
    private readonly IReportService _reportService;

    public ReportController(IReportService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>
    /// Gets report summary for a date range.
    /// </summary>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>Complete report summary with metrics.</returns>
    /// <response code="200">Returns the report summary.</response>
    /// <response code="400">If the request parameters are invalid.</response>
    [HttpGet("summary")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(ReportSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var summary = await _reportService.GetSummaryAsync(BranchId, from, to);
        return Ok(summary);
    }

    /// <summary>
    /// Downloads Excel report for a date range.
    /// </summary>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>Excel file download.</returns>
    /// <response code="200">Returns the Excel file.</response>
    /// <response code="400">If the request parameters are invalid.</response>
    [HttpGet("export/excel")]
    [Authorize(Roles = "Owner")]
    [RequiresFeature(FeatureKey.AdvancedReports)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportExcel(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var bytes = await _reportService.GenerateExcelAsync(BranchId, from, to);

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"reporte-ventas-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// Downloads PDF report for a date range.
    /// </summary>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>PDF file download.</returns>
    /// <response code="200">Returns the PDF file.</response>
    /// <response code="400">If the request parameters are invalid.</response>
    [HttpGet("export/pdf")]
    [Authorize(Roles = "Owner")]
    [RequiresFeature(FeatureKey.AdvancedReports)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportPdf(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var bytes = await _reportService.GeneratePdfAsync(BranchId, from, to);

        return File(
            bytes,
            "application/pdf",
            $"reporte-ventas-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
    }

    /// <summary>
    /// Downloads a fiscal CSV export including invoice status for each order.
    /// Columns: OrderId, Date, Total, PaymentMethod, InvoiceStatus.
    /// </summary>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>CSV file download.</returns>
    /// <response code="200">Returns the CSV file.</response>
    /// <response code="400">If the request parameters are invalid.</response>
    [HttpGet("export/fiscal-csv")]
    [Authorize(Roles = "Owner")]
    [RequiresFeature(FeatureKey.AdvancedReports)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportFiscalCsv(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var bytes = await _reportService.GenerateFiscalCsvAsync(BranchId, from, to);

        return File(
            bytes,
            "text/csv",
            $"reporte-fiscal-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv");
    }

    // ──────────────────────────────────────────
    // BDD-006: Advanced BI & Reporting
    // ──────────────────────────────────────────

    /// <summary>
    /// Returns aggregated chart data for the BI dashboard.
    /// Includes time-series sales, top products, and payment-method breakdown.
    /// Only paid, non-cancelled orders are included.
    /// </summary>
    /// <param name="from">Start date (inclusive).</param>
    /// <param name="to">End date (inclusive).</param>
    /// <param name="granularity">Time bucket: "hour", "day" (default), or "month".</param>
    /// <returns>Dashboard chart data.</returns>
    /// <response code="200">Returns the chart data.</response>
    /// <response code="400">If the request parameters are invalid.</response>
    [HttpGet("charts")]
    [Authorize(Roles = "Owner")]
    [RequiresFeature(FeatureKey.AdvancedReports)]
    [ProducesResponseType(typeof(DashboardChartsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetDashboardCharts(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to,
        [FromQuery] string granularity = "day")
    {
        try
        {
            var charts = await _reportService.GetDashboardChartsAsync(BranchId, from, to, granularity);
            return Ok(charts);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Downloads a detailed sales CSV for accounting or auditing.
    /// Columns: OrderId, Date, Total, PaymentMethods, CustomerName, Facturado.
    /// Only paid, non-cancelled orders are included.
    /// </summary>
    /// <param name="from">Start date (inclusive).</param>
    /// <param name="to">End date (inclusive).</param>
    /// <returns>CSV file download (UTF-8 with BOM).</returns>
    /// <response code="200">Returns the CSV file.</response>
    /// <response code="400">If the request parameters are invalid.</response>
    [HttpGet("export/detailed-sales-csv")]
    [Authorize(Roles = "Owner")]
    [RequiresFeature(FeatureKey.AdvancedReports)]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportDetailedSalesCsv(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        try
        {
            var csvContent = await _reportService.GetDetailedSalesCsvAsync(BranchId, from, to);
            var bytes = System.Text.Encoding.UTF8.GetBytes(csvContent);

            return File(
                bytes,
                "text/csv",
                $"ventas-detallado-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
