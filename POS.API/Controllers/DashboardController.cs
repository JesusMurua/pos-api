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
    /// Returns dashboard summary for a specific local calendar day in the
    /// branch's timezone. Defaults to today in UTC wall-clock when omitted.
    /// </summary>
    /// <param name="date">Local calendar date to summarize.</param>
    /// <returns>Dashboard summary with sales, cancellations, top products, recent orders.</returns>
    /// <response code="200">Returns the dashboard summary.</response>
    [HttpGet("summary")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(DashboardSummaryDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummary([FromQuery] DateOnly? date)
    {
        var target = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var summary = await _dashboardService.GetSummaryAsync(BranchId, target);
        return Ok(summary);
    }
}
