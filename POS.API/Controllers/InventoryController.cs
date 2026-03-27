using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing inventory items and movements.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class InventoryController : BaseApiController
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    /// <summary>
    /// Gets all active inventory items for the current branch.
    /// </summary>
    /// <returns>A list of inventory items.</returns>
    /// <response code="200">Returns the list of inventory items.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(IEnumerable<InventoryItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAll()
    {
        var items = await _inventoryService.GetAllAsync(BranchId);
        return Ok(items);
    }

    /// <summary>
    /// Gets product IDs whose inventory items are out of stock.
    /// </summary>
    /// <returns>A list of product IDs with depleted inventory.</returns>
    /// <response code="200">Returns the list of out-of-stock product IDs.</response>
    [HttpGet("out-of-stock-products")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(IEnumerable<int>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOutOfStockProducts()
    {
        var productIds = await _inventoryService.GetOutOfStockProductIdsAsync(BranchId);
        return Ok(productIds);
    }

    /// <summary>
    /// Gets an inventory item by its identifier.
    /// </summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <returns>The requested inventory item.</returns>
    /// <response code="200">Returns the requested inventory item.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpGet("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(InventoryItem), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        var item = await _inventoryService.GetByIdAsync(id);
        return Ok(item);
    }

    /// <summary>
    /// Gets inventory items with stock at or below threshold.
    /// </summary>
    /// <returns>A list of low-stock inventory items.</returns>
    /// <response code="200">Returns the list of low-stock items.</response>
    [HttpGet("low-stock")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<InventoryItem>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLowStock()
    {
        var items = await _inventoryService.GetLowStockAsync(BranchId);
        return Ok(items);
    }

    /// <summary>
    /// Gets all movements for an inventory item.
    /// </summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <returns>A list of inventory movements.</returns>
    /// <response code="200">Returns the list of movements.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpGet("{id}/movements")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<InventoryMovement>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMovements(int id)
    {
        var movements = await _inventoryService.GetMovementsAsync(id);
        return Ok(movements);
    }

    /// <summary>
    /// Creates a new inventory item.
    /// </summary>
    /// <param name="item">The inventory item data.</param>
    /// <returns>The created inventory item identifier.</returns>
    /// <response code="200">Returns the created item identifier.</response>
    /// <response code="400">If the item data is invalid.</response>
    [HttpPost("create")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] InventoryItem item)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await _inventoryService.CreateAsync(item);
        return Ok(new { id = created.Id });
    }

    /// <summary>
    /// Updates an existing inventory item.
    /// </summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <param name="item">The updated inventory item data.</param>
    /// <returns>The updated inventory item.</returns>
    /// <response code="200">Returns the updated item.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(InventoryItem), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] InventoryItem item)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _inventoryService.UpdateAsync(id, item);
        return Ok(updated);
    }

    /// <summary>
    /// Soft deletes an inventory item.
    /// </summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <returns>Success status.</returns>
    /// <response code="200">Returns success status.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        await _inventoryService.DeleteAsync(id);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Adds a movement to an inventory item and recalculates stock.
    /// </summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <param name="request">The movement data.</param>
    /// <returns>The created movement.</returns>
    /// <response code="200">Returns the created movement.</response>
    /// <response code="400">If the movement type is invalid.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpPost("{id}/movement")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(InventoryMovement), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddMovement(int id, [FromBody] AddInventoryMovementRequest request)
    {
        var movement = await _inventoryService.AddMovementAsync(
            id, request.Type, request.Quantity, request.Reason, null);
        return Ok(movement);
    }

    /// <summary>
    /// Gets all consumption rules for a product.
    /// </summary>
    /// <param name="productId">The product identifier.</param>
    /// <returns>A list of consumption rules with inventory item details.</returns>
    /// <response code="200">Returns the list of consumption rules.</response>
    [HttpGet("consumption/{productId}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<ProductConsumption>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConsumption(int productId)
    {
        var consumptions = await _inventoryService.GetConsumptionByProductAsync(productId);
        return Ok(consumptions);
    }

    /// <summary>
    /// Creates or updates a product consumption rule.
    /// </summary>
    /// <param name="request">The consumption rule data.</param>
    /// <returns>The created or updated consumption rule.</returns>
    /// <response code="200">Returns the consumption rule.</response>
    /// <response code="400">If the request data is invalid.</response>
    [HttpPost("consumption")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(ProductConsumption), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateConsumption([FromBody] CreateConsumptionRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var consumption = await _inventoryService.CreateConsumptionAsync(
            request.ProductId, request.InventoryItemId, request.QuantityPerSale);
        return Ok(consumption);
    }

    /// <summary>
    /// Deletes a product consumption rule.
    /// </summary>
    /// <param name="id">The consumption rule identifier.</param>
    /// <returns>Success status.</returns>
    /// <response code="200">Returns success status.</response>
    /// <response code="404">If the consumption rule is not found.</response>
    [HttpDelete("consumption/{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteConsumption(int id)
    {
        await _inventoryService.DeleteConsumptionAsync(id);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Deducts inventory based on products sold. Best-effort — always returns 200.
    /// </summary>
    /// <param name="request">The sale data with order ID and items sold.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Always returns success — failures are logged internally.</response>
    [HttpPost("deduct-sale")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> DeductSale([FromBody] DeductSaleRequest request)
    {
        await _inventoryService.DeductFromSaleAsync(request.OrderId, request.Items);
        return Ok(new { success = true });
    }
}

/// <summary>
/// Request body for adding an inventory movement.
/// </summary>
public class AddInventoryMovementRequest
{
    public string Type { get; set; } = null!;
    public decimal Quantity { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// Request body for creating a product consumption rule.
/// </summary>
public class CreateConsumptionRequest
{
    public int ProductId { get; set; }
    public int InventoryItemId { get; set; }
    public decimal QuantityPerSale { get; set; }
}
