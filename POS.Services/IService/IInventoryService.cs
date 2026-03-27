using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing inventory items and movements.
/// </summary>
public interface IInventoryService
{
    /// <summary>
    /// Gets all active inventory items for a branch.
    /// </summary>
    Task<IEnumerable<InventoryItem>> GetAllAsync(int branchId);

    /// <summary>
    /// Gets an inventory item by its identifier.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when item not found.</exception>
    Task<InventoryItem> GetByIdAsync(int id);

    /// <summary>
    /// Gets inventory items with stock at or below threshold.
    /// </summary>
    Task<IEnumerable<InventoryItem>> GetLowStockAsync(int branchId);

    /// <summary>
    /// Creates a new inventory item.
    /// </summary>
    Task<InventoryItem> CreateAsync(InventoryItem item);

    /// <summary>
    /// Updates an existing inventory item.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when item not found.</exception>
    Task<InventoryItem> UpdateAsync(int id, InventoryItem item);

    /// <summary>
    /// Soft deletes an inventory item by setting IsActive to false.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when item not found.</exception>
    Task<bool> DeleteAsync(int id);

    /// <summary>
    /// Adds a movement and recalculates current stock.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when item not found.</exception>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">Thrown when type is invalid.</exception>
    Task<InventoryMovement> AddMovementAsync(int itemId, string type, decimal quantity, string? reason, string? orderId);

    /// <summary>
    /// Gets all movements for an inventory item.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when item not found.</exception>
    Task<IEnumerable<InventoryMovement>> GetMovementsAsync(int itemId);

    /// <summary>
    /// Gets all consumption rules for a product, including inventory item details.
    /// </summary>
    Task<IEnumerable<ProductConsumption>> GetConsumptionByProductAsync(int productId);

    /// <summary>
    /// Creates or updates a product consumption rule.
    /// </summary>
    Task<ProductConsumption> CreateConsumptionAsync(int productId, int inventoryItemId, decimal quantityPerSale);

    /// <summary>
    /// Deletes a product consumption rule.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when consumption not found.</exception>
    Task<bool> DeleteConsumptionAsync(int id);

    /// <summary>
    /// Deducts inventory based on products sold. Best-effort — never throws.
    /// </summary>
    Task DeductFromSaleAsync(string orderId, List<SaleItem> items);

    /// <summary>
    /// Gets product IDs whose inventory items have zero or negative stock.
    /// </summary>
    Task<IEnumerable<int>> GetOutOfStockProductIdsAsync(int branchId);

    /// <summary>
    /// Gets inventory movements for a product with TrackStock enabled, ordered by CreatedAt descending.
    /// </summary>
    Task<IEnumerable<InventoryMovement>> GetProductMovementsAsync(int productId);
}
