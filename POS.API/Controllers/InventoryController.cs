using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing inventory items, recipe (ProductConsumption) rules,
/// and the immutable ledger of inventory movements.
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

    // ──────────────────────────────────────────────────────────────
    // Inventory Item CRUD
    // ──────────────────────────────────────────────────────────────

    /// <summary>Gets all active inventory items for the current branch.</summary>
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

    /// <summary>Gets product IDs whose inventory items are out of stock.</summary>
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

    /// <summary>Gets an inventory item by its identifier.</summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <returns>The requested inventory item.</returns>
    /// <response code="200">Returns the requested inventory item.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpGet("{id:int}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(InventoryItem), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var item = await _inventoryService.GetByIdAsync(id);
            return Ok(item);
        }
        catch (POS.Domain.Exceptions.NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>Gets inventory items with stock at or below their low stock threshold.</summary>
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

    /// <summary>Creates a new inventory item.</summary>
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

        item.BranchId = BranchId;

        var created = await _inventoryService.CreateAsync(item);
        return Ok(new { id = created.Id });
    }

    /// <summary>Updates an existing inventory item's metadata.</summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <param name="item">The updated inventory item data.</param>
    /// <returns>The updated inventory item.</returns>
    /// <response code="200">Returns the updated item.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpPut("{id:int}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(InventoryItem), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] InventoryItem item)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var updated = await _inventoryService.UpdateAsync(id, item);
            return Ok(updated);
        }
        catch (POS.Domain.Exceptions.NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>Soft-deletes an inventory item (sets IsActive to false).</summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <returns>Success status.</returns>
    /// <response code="200">Returns success status.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _inventoryService.DeleteAsync(id);
            return Ok(new { success = true });
        }
        catch (POS.Domain.Exceptions.NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Legacy Movement Endpoint (backwards compatibility)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a movement using the legacy string-type API ("in", "out", "adjustment").
    /// Prefer the typed endpoints /purchase, /waste, /adjustment for new integrations.
    /// </summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <param name="request">The movement data.</param>
    /// <returns>The created movement.</returns>
    /// <response code="200">Returns the created movement.</response>
    /// <response code="400">If the movement type is invalid.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpPost("{id:int}/movement")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(InventoryMovement), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddMovement(int id, [FromBody] AddInventoryMovementRequest request)
    {
        try
        {
            var movement = await _inventoryService.AddMovementAsync(
                id, request.Type, request.Quantity, request.Reason, null);
            return Ok(movement);
        }
        catch (POS.Domain.Exceptions.ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (POS.Domain.Exceptions.NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>Gets all movements for an inventory item ordered by date descending.</summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <returns>A list of inventory movements.</returns>
    /// <response code="200">Returns the list of movements.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpGet("{id:int}/movements")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<InventoryMovement>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMovements(int id)
    {
        try
        {
            var movements = await _inventoryService.GetMovementsAsync(id);
            return Ok(movements);
        }
        catch (POS.Domain.Exceptions.NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Typed Ledger Endpoints (Phase 18)
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a stock purchase from a supplier.
    /// Adds the specified quantity to the ingredient's current stock and creates
    /// an immutable Purchase ledger entry.
    /// </summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <param name="request">Purchase details.</param>
    /// <returns>The created ledger movement.</returns>
    /// <response code="200">Returns the created movement.</response>
    /// <response code="400">If the request is invalid.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpPost("{id:int}/purchase")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(InventoryMovement), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegisterPurchase(int id, [FromBody] RegisterPurchaseRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var movement = await _inventoryService.RegisterPurchaseAsync(
                id,
                request.Quantity,
                request.CostCentsPerUnit,
                request.Note,
                CurrentUserName);

            return Ok(movement);
        }
        catch (POS.Domain.Exceptions.ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (POS.Domain.Exceptions.NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Records a controlled write-off (waste) for an inventory item.
    /// Subtracts the specified quantity from current stock.
    /// A reason description is required.
    /// </summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <param name="request">Waste details including mandatory reason.</param>
    /// <returns>The created ledger movement.</returns>
    /// <response code="200">Returns the created movement.</response>
    /// <response code="400">If the request is invalid or reason is missing.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpPost("{id:int}/waste")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(InventoryMovement), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegisterWaste(int id, [FromBody] RegisterWasteRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var movement = await _inventoryService.RegisterWasteAsync(
                id,
                request.Quantity,
                request.Reason,
                CurrentUserName);

            return Ok(movement);
        }
        catch (POS.Domain.Exceptions.ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (POS.Domain.Exceptions.NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Applies a manual delta adjustment to an ingredient's stock.
    /// Use a positive delta to add stock, negative to subtract.
    /// A reason description is required.
    /// </summary>
    /// <param name="id">The inventory item identifier.</param>
    /// <param name="request">Adjustment details including mandatory reason and delta.</param>
    /// <returns>The created ledger movement.</returns>
    /// <response code="200">Returns the created movement.</response>
    /// <response code="400">If delta is zero or reason is missing.</response>
    /// <response code="404">If the item is not found.</response>
    [HttpPost("{id:int}/adjustment")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(InventoryMovement), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RegisterAdjustment(int id, [FromBody] RegisterAdjustmentRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var movement = await _inventoryService.RegisterManualAdjustmentAsync(
                id,
                request.Delta,
                request.Reason,
                CurrentUserName);

            return Ok(movement);
        }
        catch (POS.Domain.Exceptions.ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (POS.Domain.Exceptions.NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Returns the movement history for the branch with optional filters.
    /// Only ingredient-path movements are returned (TrackStock product movements
    /// are available via GET /api/products/{id}/movements).
    /// </summary>
    /// <param name="inventoryItemId">Filter to a specific ingredient. Omit for all items.</param>
    /// <param name="type">Filter by transaction type (Purchase, ConsumeFromSale, Waste, ManualAdjustment, InitialCount).</param>
    /// <param name="from">Start date, UTC (inclusive).</param>
    /// <param name="to">End date, UTC (inclusive).</param>
    /// <returns>A list of inventory movements ordered by date descending.</returns>
    /// <response code="200">Returns the list of movements.</response>
    [HttpGet("movements/history")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<InventoryMovement>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMovementHistory(
        [FromQuery] int? inventoryItemId,
        [FromQuery] InventoryTransactionType? type,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to)
    {
        var movements = await _inventoryService.GetMovementHistoryAsync(
            BranchId, inventoryItemId, type, from, to);
        return Ok(movements);
    }

    // ──────────────────────────────────────────────────────────────
    // Recipe (ProductConsumption) Endpoints
    // ──────────────────────────────────────────────────────────────

    /// <summary>Gets all consumption rules (recipe lines) for a product.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <returns>A list of consumption rules with inventory item details.</returns>
    /// <response code="200">Returns the list of consumption rules.</response>
    [HttpGet("consumption/{productId:int}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<ProductConsumption>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetConsumption(int productId)
    {
        var consumptions = await _inventoryService.GetConsumptionByProductAsync(productId);
        return Ok(consumptions);
    }

    /// <summary>
    /// Creates or updates a product consumption rule (recipe line).
    /// If the same product–ingredient pair already exists, the quantity is updated.
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

    /// <summary>Deletes a product consumption rule by identifier.</summary>
    /// <param name="id">The consumption rule identifier.</param>
    /// <returns>Success status.</returns>
    /// <response code="200">Returns success status.</response>
    /// <response code="404">If the consumption rule is not found.</response>
    [HttpDelete("consumption/{id:int}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteConsumption(int id)
    {
        try
        {
            await _inventoryService.DeleteConsumptionAsync(id);
            return Ok(new { success = true });
        }
        catch (POS.Domain.Exceptions.NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────────
    // Sync Engine Helper Endpoint
    // ──────────────────────────────────────────────────────────────

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

    // ──────────────────────────────────────────────────────────────
    // Private helpers
    // ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the current user's display name from JWT claims.
    /// Falls back to the numeric UserId when the Name claim is absent.
    /// </summary>
    private string CurrentUserName =>
        User.Identity?.Name ?? UserId.ToString();
}

// ──────────────────────────────────────────────────────────────────
// Request DTOs
// ──────────────────────────────────────────────────────────────────

/// <summary>Request body for the legacy generic movement endpoint.</summary>
public class AddInventoryMovementRequest
{
    /// <summary>Movement direction: "in", "out", or "adjustment".</summary>
    public string Type { get; set; } = null!;

    /// <summary>Quantity involved. Must be positive.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Optional free-text note.</summary>
    public string? Reason { get; set; }
}

/// <summary>Request body for registering a stock purchase.</summary>
public class RegisterPurchaseRequest
{
    /// <summary>Units received from the supplier. Must be positive.</summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// New cost per unit in cents. When provided, updates the ingredient's <c>CostCents</c>.
    /// Omit to keep the current cost unchanged.
    /// </summary>
    public int? CostCentsPerUnit { get; set; }

    /// <summary>Optional note (e.g., supplier name, invoice number).</summary>
    public string? Note { get; set; }
}

/// <summary>Request body for registering a waste (write-off) event.</summary>
public class RegisterWasteRequest
{
    /// <summary>Units written off. Must be positive.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Mandatory description of the cause (e.g., "Producto caducado", "Accidente").</summary>
    public string Reason { get; set; } = null!;
}

/// <summary>Request body for applying a manual stock adjustment.</summary>
public class RegisterAdjustmentRequest
{
    /// <summary>
    /// Amount to add (positive) or subtract (negative) from the current stock.
    /// Must be non-zero.
    /// </summary>
    public decimal Delta { get; set; }

    /// <summary>Mandatory explanation for the adjustment (e.g., "Conteo físico 2026-04-03").</summary>
    public string Reason { get; set; } = null!;
}

/// <summary>Request body for creating a product consumption (recipe) rule.</summary>
public class CreateConsumptionRequest
{
    /// <summary>Product that consumes the ingredient.</summary>
    public int ProductId { get; set; }

    /// <summary>Ingredient consumed by the product.</summary>
    public int InventoryItemId { get; set; }

    /// <summary>Units of the ingredient consumed per one unit sold.</summary>
    public decimal QuantityPerSale { get; set; }
}
