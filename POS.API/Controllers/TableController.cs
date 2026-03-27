using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for restaurant table management.
/// </summary>
[Route("api/[controller]")]
public class TableController : BaseApiController
{
    private readonly ITableService _tableService;
    private readonly IOrderService _orderService;

    public TableController(ITableService tableService, IOrderService orderService)
    {
        _tableService = tableService;
        _orderService = orderService;
    }

    /// <summary>
    /// Gets all tables for the current branch.
    /// </summary>
    /// <param name="includeInactive">Whether to include inactive tables.</param>
    /// <returns>A list of tables.</returns>
    /// <response code="200">Returns the list of tables.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager,Cashier,Waiter,Kitchen")]
    [ProducesResponseType(typeof(IEnumerable<RestaurantTable>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBranch([FromQuery] bool includeInactive = false)
    {
        var tables = await _tableService.GetByBranchAsync(BranchId, includeInactive);
        return Ok(tables);
    }

    /// <summary>
    /// Creates a new table.
    /// </summary>
    /// <param name="table">The table data to create.</param>
    /// <returns>The created table identifier.</returns>
    /// <response code="200">Returns the created table identifier.</response>
    /// <response code="400">If a table with the same name already exists.</response>
    [HttpPost]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] RestaurantTable table)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await _tableService.CreateAsync(BranchId, table);
        return Ok(new { id = created.Id });
    }

    /// <summary>
    /// Updates an existing table.
    /// </summary>
    /// <param name="id">The table identifier.</param>
    /// <param name="table">The updated table data.</param>
    /// <returns>The updated table.</returns>
    /// <response code="200">Returns the updated table.</response>
    /// <response code="404">If the table is not found.</response>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(RestaurantTable), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] RestaurantTable table)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _tableService.UpdateAsync(id, table);
        return Ok(updated);
    }

    /// <summary>
    /// Toggles the active status of a table.
    /// </summary>
    /// <param name="id">The table identifier.</param>
    /// <returns>The new active status.</returns>
    /// <response code="200">Returns the new active status.</response>
    /// <response code="404">If the table is not found.</response>
    [HttpPatch("{id}/toggle")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Toggle(int id)
    {
        var isActive = await _tableService.ToggleActiveAsync(id);
        return Ok(new { isActive });
    }

    /// <summary>
    /// Updates the occupancy status of a table.
    /// </summary>
    /// <param name="id">The table identifier.</param>
    /// <param name="request">The status update request.</param>
    /// <returns>The updated table.</returns>
    /// <response code="200">Returns the updated table.</response>
    /// <response code="400">If the status value is invalid.</response>
    /// <response code="404">If the table is not found.</response>
    [HttpPatch("{id}/status")]
    [Authorize(Roles = "Owner,Manager,Cashier,Waiter")]
    [ProducesResponseType(typeof(RestaurantTable), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] TableStatusRequest request)
    {
        if (request.Status?.ToLowerInvariant() == "available")
        {
            var activeOrders = await _orderService.GetActiveByTableAsync(id);
            var count = activeOrders.Count();
            if (count > 0)
                return BadRequest(new { message = $"No se puede liberar la mesa, tiene {count} orden(es) sin completar" });
        }

        var table = await _tableService.UpdateStatusAsync(id, request.Status!);
        return Ok(table);
    }
}

/// <summary>
/// Request body for updating table status.
/// </summary>
public class TableStatusRequest
{
    public string Status { get; set; } = null!;
}
