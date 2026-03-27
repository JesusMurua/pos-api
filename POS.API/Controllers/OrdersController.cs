using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing orders and offline sync.
/// </summary>
[Route("api/[controller]")]
public class OrdersController : BaseApiController
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
    [Authorize(Roles = "Owner,Manager,Cashier,Waiter")]
    [ProducesResponseType(typeof(SyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sync([FromBody] List<SyncOrderRequest> orders)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _orderService.SyncOrdersAsync(orders);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves orders for the current branch on a specific date.
    /// </summary>
    /// <param name="date">The date to filter by.</param>
    /// <returns>A list of orders.</returns>
    /// <response code="200">Returns the list of orders.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(IEnumerable<Order>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBranchAndDate([FromQuery] DateTime date)
    {
        var orders = await _orderService.GetByBranchAndDateAsync(BranchId, date);
        return Ok(orders);
    }

    /// <summary>
    /// Retrieves daily KPI summary for the current branch.
    /// </summary>
    /// <param name="date">The date to summarize.</param>
    /// <returns>Order data for daily summary.</returns>
    /// <response code="200">Returns the daily summary data.</response>
    [HttpGet("summary")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(IEnumerable<Order>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDailySummary([FromQuery] DateTime date)
    {
        var orders = await _orderService.GetDailySummaryAsync(BranchId, date);
        return Ok(orders);
    }

    /// <summary>
    /// Gets the last order number for the current branch.
    /// </summary>
    /// <returns>The last order number, or 0 if no orders exist.</returns>
    /// <response code="200">Returns the last order number.</response>
    [HttpGet("last-number")]
    [Authorize(Roles = "Owner,Manager,Cashier,Waiter")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLastOrderNumber()
    {
        var lastNumber = await _orderService.GetLastOrderNumberAsync(BranchId);
        return Ok(new { lastOrderNumber = lastNumber });
    }

    /// <summary>
    /// Gets active (non-cancelled) orders for a specific table.
    /// </summary>
    /// <param name="tableId">The table identifier.</param>
    /// <returns>A list of active orders for the table.</returns>
    /// <response code="200">Returns the list of active orders, or empty array if none.</response>
    [HttpGet("by-table/{tableId}")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByTable(int tableId)
    {
        var orders = await _orderService.GetActiveByTableAsync(tableId);
        return Ok(orders);
    }

    /// <summary>
    /// Cancels an order.
    /// </summary>
    /// <param name="id">The order UUID.</param>
    /// <param name="request">Cancellation reason and optional notes.</param>
    /// <returns>The cancelled order.</returns>
    /// <response code="200">Returns the cancelled order.</response>
    /// <response code="400">If the order is already cancelled.</response>
    /// <response code="404">If the order is not found.</response>
    [HttpPatch("{id}/cancel")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(string id, [FromBody] CancelOrderRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userName = User.Identity?.Name ?? "Unknown";
        var order = await _orderService.CancelAsync(id, request.Reason, request.Notes, userName);
        return Ok(order);
    }

    /// <summary>
    /// Updates the kitchen status of an order.
    /// </summary>
    /// <param name="id">The order UUID.</param>
    /// <param name="request">The new kitchen status.</param>
    /// <returns>The updated order.</returns>
    /// <response code="200">Returns the updated order.</response>
    /// <response code="400">If the status value is invalid.</response>
    /// <response code="404">If the order is not found.</response>
    [HttpPatch("{id}/kitchen-status")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateKitchenStatus(string id, [FromBody] UpdateKitchenStatusRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var validStatuses = new[] { "Preparing", "Ready", "Delivered" };
        if (!validStatuses.Contains(request.Status))
            return BadRequest(new { message = "Status must be 'Preparing', 'Ready', or 'Delivered'" });

        var order = await _orderService.UpdateKitchenStatusAsync(id, request.Status);
        return Ok(order);
    }
}

/// <summary>
/// Request body for updating kitchen status.
/// </summary>
public class UpdateKitchenStatusRequest
{
    public string Status { get; set; } = null!;
}
