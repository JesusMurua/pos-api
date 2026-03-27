using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
}
