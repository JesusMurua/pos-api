using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for sales reports and export.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize(Roles = "Owner")]
public class ReportController : ControllerBase
{
    private readonly IReportService _reportService;

    public ReportController(IReportService reportService)
    {
        _reportService = reportService;
    }

    /// <summary>
    /// Gets report summary for a date range.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>Complete report summary with metrics.</returns>
    /// <response code="200">Returns the report summary.</response>
    /// <response code="400">If the request parameters are invalid.</response>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ReportSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] int branchId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var summary = await _reportService.GetSummaryAsync(branchId, from, to);
        return Ok(summary);
    }

    /// <summary>
    /// Downloads Excel report for a date range.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>Excel file download.</returns>
    /// <response code="200">Returns the Excel file.</response>
    /// <response code="400">If the request parameters are invalid.</response>
    [HttpGet("export/excel")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportExcel(
        [FromQuery] int branchId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var bytes = await _reportService.GenerateExcelAsync(branchId, from, to);

        return File(
            bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"reporte-ventas-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// Downloads PDF report for a date range.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>PDF file download.</returns>
    /// <response code="200">Returns the PDF file.</response>
    /// <response code="400">If the request parameters are invalid.</response>
    [HttpGet("export/pdf")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ExportPdf(
        [FromQuery] int branchId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var bytes = await _reportService.GeneratePdfAsync(branchId, from, to);

        return File(
            bytes,
            "application/pdf",
            $"reporte-ventas-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.pdf");
    }
}
