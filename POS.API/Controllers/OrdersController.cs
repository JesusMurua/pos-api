using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing orders and offline sync.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    /// <summary>
    /// Syncs a batch of offline orders from the frontend.
    /// Idempotent — duplicate UUIDs are skipped without error.
    /// </summary>
    /// <param name="orders">The list of orders to sync.</param>
    /// <returns>A sync summary with synced, skipped, and failed counts.</returns>
    /// <response code="200">Returns the sync result summary.</response>
    /// <response code="400">If the request body is invalid.</response>
    [HttpPost("sync")]
    [ProducesResponseType(typeof(SyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sync([FromBody] List<SyncOrderRequest> orders)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _orderService.SyncOrdersAsync(orders);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves orders for a branch on a specific date.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="date">The date to filter by.</param>
    /// <returns>A list of orders.</returns>
    /// <response code="200">Returns the list of orders.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<Order>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBranchAndDate(
        [FromQuery] int branchId,
        [FromQuery] DateTime date)
    {
        var orders = await _orderService.GetByBranchAndDateAsync(branchId, date);
        return Ok(orders);
    }

    /// <summary>
    /// Retrieves daily KPI summary for a branch.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="date">The date to summarize.</param>
    /// <returns>Order data for daily summary.</returns>
    /// <response code="200">Returns the daily summary data.</response>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(IEnumerable<Order>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDailySummary(
        [FromQuery] int branchId,
        [FromQuery] DateTime date)
    {
        var orders = await _orderService.GetDailySummaryAsync(branchId, date);
        return Ok(orders);
    }
}
