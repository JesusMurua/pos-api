using POS.Domain.Enums;
using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing inventory items, recipes, and ledger movements.
/// </summary>
public interface IInventoryService
{
    #region Inventory Item CRUD

    /// <summary>Gets all active inventory items for a branch.</summary>
    Task<IEnumerable<InventoryItem>> GetAllAsync(int branchId);

    /// <summary>Gets an inventory item by its identifier.</summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Item not found.</exception>
    Task<InventoryItem> GetByIdAsync(int id);

    /// <summary>Gets inventory items with stock at or below their low stock threshold.</summary>
    Task<IEnumerable<InventoryItem>> GetLowStockAsync(int branchId);

    /// <summary>Creates a new inventory item.</summary>
    Task<InventoryItem> CreateAsync(InventoryItem item);

    /// <summary>Updates an existing inventory item's metadata (name, unit, cost, threshold).</summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Item not found.</exception>
    Task<InventoryItem> UpdateAsync(int id, InventoryItem item);

    /// <summary>Soft-deletes an inventory item by setting <c>IsActive</c> to <c>false</c>.</summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Item not found.</exception>
    Task<bool> DeleteAsync(int id);

    #endregion

    #region Legacy Movement (kept for backwards compatibility)

    /// <summary>
    /// Adds a movement using the legacy string-type API ("in", "out", "adjustment")
    /// and recalculates current stock. Kept for backwards compatibility.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Item not found.</exception>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">Invalid type string.</exception>
    Task<InventoryMovement> AddMovementAsync(int itemId, string type, decimal quantity, string? reason, string? orderId);

    /// <summary>Gets all movements for an inventory item ordered by <c>CreatedAt</c> descending.</summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Item not found.</exception>
    Task<IEnumerable<InventoryMovement>> GetMovementsAsync(int itemId);

    #endregion

    #region Typed Ledger Operations (Phase 18)

    /// <summary>
    /// Records a stock purchase from a supplier.
    /// Adds <paramref name="quantity"/> to the item's current stock and creates
    /// an immutable <see cref="InventoryTransactionType.Purchase"/> ledger entry.
    /// </summary>
    /// <param name="inventoryItemId">Target ingredient.</param>
    /// <param name="quantity">Units received. Must be positive.</param>
    /// <param name="costCentsPerUnit">
    /// New cost per unit in cents. When provided, updates <c>InventoryItem.CostCents</c>.
    /// Pass <c>null</c> to keep the existing cost unchanged.
    /// </param>
    /// <param name="note">Optional note about this purchase.</param>
    /// <param name="createdBy">Username or system identifier originating the request.</param>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Item not found.</exception>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">Quantity is not positive.</exception>
    Task<InventoryMovement> RegisterPurchaseAsync(
        int inventoryItemId,
        decimal quantity,
        int? costCentsPerUnit,
        string? note,
        string createdBy);

    /// <summary>
    /// Records a controlled write-off (expired, damaged, or spilled stock).
    /// Subtracts <paramref name="quantity"/> from current stock and creates
    /// an immutable <see cref="InventoryTransactionType.Waste"/> ledger entry.
    /// </summary>
    /// <param name="inventoryItemId">Target ingredient.</param>
    /// <param name="quantity">Units written off. Must be positive.</param>
    /// <param name="reason">Mandatory description of the cause (e.g., "Producto caducado").</param>
    /// <param name="createdBy">Username or system identifier originating the request.</param>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Item not found.</exception>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">Quantity not positive or reason empty.</exception>
    Task<InventoryMovement> RegisterWasteAsync(
        int inventoryItemId,
        decimal quantity,
        string reason,
        string createdBy);

    /// <summary>
    /// Applies a manual delta to the stock after a physical inventory count.
    /// Creates an immutable <see cref="InventoryTransactionType.ManualAdjustment"/> ledger entry.
    /// </summary>
    /// <param name="inventoryItemId">Target ingredient.</param>
    /// <param name="delta">
    /// Amount to add (positive) or subtract (negative) from the current stock.
    /// Must be non-zero.
    /// </param>
    /// <param name="reason">Mandatory explanation for the adjustment.</param>
    /// <param name="createdBy">Username or system identifier originating the request.</param>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Item not found.</exception>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">Delta is zero or reason is empty.</exception>
    Task<InventoryMovement> RegisterManualAdjustmentAsync(
        int inventoryItemId,
        decimal delta,
        string reason,
        string createdBy);

    /// <summary>
    /// Returns inventory movements for a branch with optional filters.
    /// Only ingredient-path movements (<c>InventoryItemId != null</c>) are returned.
    /// Results are ordered by <c>CreatedAt</c> descending.
    /// </summary>
    /// <param name="branchId">Branch scope.</param>
    /// <param name="inventoryItemId">Filter to a specific ingredient. <c>null</c> returns all.</param>
    /// <param name="type">Filter by transaction type. <c>null</c> returns all types.</param>
    /// <param name="from">Lower bound for <c>CreatedAt</c> (inclusive). UTC.</param>
    /// <param name="to">Upper bound for <c>CreatedAt</c> (inclusive). UTC.</param>
    Task<IEnumerable<InventoryMovement>> GetMovementHistoryAsync(
        int branchId,
        int? inventoryItemId,
        InventoryTransactionType? type,
        DateTime? from,
        DateTime? to);

    #endregion

    #region Recipe (ProductConsumption)

    /// <summary>Gets all consumption rules for a product, including ingredient details.</summary>
    Task<IEnumerable<ProductConsumption>> GetConsumptionByProductAsync(int productId);

    /// <summary>
    /// Creates or updates a product consumption rule (recipe line).
    /// If the same <paramref name="productId"/>/<paramref name="inventoryItemId"/> pair already
    /// exists, the <c>QuantityPerSale</c> is updated in place.
    /// </summary>
    Task<ProductConsumption> CreateConsumptionAsync(int productId, int inventoryItemId, decimal quantityPerSale);

    /// <summary>Deletes a product consumption rule by its identifier.</summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Rule not found.</exception>
    Task<bool> DeleteConsumptionAsync(int id);

    #endregion

    #region Sync Engine Integration

    /// <summary>
    /// Deducts inventory based on items sold in a single order. Best-effort — never throws.
    /// </summary>
    Task DeductFromSaleAsync(string orderId, List<SaleItem> items);

    /// <summary>
    /// Batch deducts inventory for multiple orders. Best-effort — never throws.
    /// All DB lookups are batched (no N+1); movements are bulk-inserted.
    /// </summary>
    Task DeductFromOrdersBatchAsync(List<Order> orders);

    #endregion

    #region Utilities

    /// <summary>
    /// Returns IDs of products whose linked inventory items have zero or negative stock.
    /// Uses a single batch query with no N+1.
    /// </summary>
    Task<IEnumerable<int>> GetOutOfStockProductIdsAsync(int branchId);

    /// <summary>
    /// Returns inventory movements for a product with <c>TrackStock = true</c>,
    /// ordered by <c>CreatedAt</c> descending.
    /// </summary>
    Task<IEnumerable<InventoryMovement>> GetProductMovementsAsync(int productId);

    #endregion
}
