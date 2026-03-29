using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Dashboard summary for daily sales and KPIs.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class DashboardController : BaseApiController
{
    private readonly IDashboardService _dashboardService;

    public DashboardController(IDashboardService dashboardService)
    {
        _dashboardService = dashboardService;
    }

    /// <summary>
    /// Returns dashboard summary for a specific date.
    /// </summary>
    /// <param name="date">Date to summarize. Defaults to today UTC.</param>
    /// <returns>Dashboard summary with sales, cancellations, top products, recent orders.</returns>
    /// <response code="200">Returns the dashboard summary.</response>
    [HttpGet("summary")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(DashboardSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary([FromQuery] DateTime? date)
    {
        var summary = await _dashboardService.GetSummaryAsync(BranchId, date ?? DateTime.UtcNow);
        return Ok(summary);
    }
}
