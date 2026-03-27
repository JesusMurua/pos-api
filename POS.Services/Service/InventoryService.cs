using Microsoft.Extensions.Logging;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Implements inventory management operations.
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InventoryService> _logger;

    private static readonly string[] ValidMovementTypes = { "in", "out", "adjustment" };

    public InventoryService(IUnitOfWork unitOfWork, ILogger<InventoryService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    #region Public API Methods

    /// <summary>
    /// Gets all active inventory items for a branch.
    /// </summary>
    public async Task<IEnumerable<InventoryItem>> GetAllAsync(int branchId)
    {
        return await _unitOfWork.Inventory.GetAllByBranchAsync(branchId);
    }

    /// <summary>
    /// Gets an inventory item by its identifier.
    /// </summary>
    public async Task<InventoryItem> GetByIdAsync(int id)
    {
        var item = await _unitOfWork.Inventory.GetByIdAsync(id);

        if (item == null)
            throw new NotFoundException($"Inventory item with id {id} not found");

        return item;
    }

    /// <summary>
    /// Gets inventory items with stock at or below threshold.
    /// </summary>
    public async Task<IEnumerable<InventoryItem>> GetLowStockAsync(int branchId)
    {
        return await _unitOfWork.Inventory.GetLowStockAsync(branchId);
    }

    /// <summary>
    /// Creates a new inventory item.
    /// </summary>
    public async Task<InventoryItem> CreateAsync(InventoryItem item)
    {
        item.CreatedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.Inventory.AddAsync(item);
        await _unitOfWork.SaveChangesAsync();

        return item;
    }

    /// <summary>
    /// Updates an existing inventory item.
    /// </summary>
    public async Task<InventoryItem> UpdateAsync(int id, InventoryItem item)
    {
        var existing = await _unitOfWork.Inventory.GetByIdAsync(id);

        if (existing == null)
            throw new NotFoundException($"Inventory item with id {id} not found");

        existing.Name = item.Name;
        existing.Unit = item.Unit;
        existing.LowStockThreshold = item.LowStockThreshold;
        existing.CostCents = item.CostCents;
        existing.IsActive = item.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Inventory.Update(existing);
        await _unitOfWork.SaveChangesAsync();

        return existing;
    }

    /// <summary>
    /// Soft deletes an inventory item by setting IsActive to false.
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        var item = await _unitOfWork.Inventory.GetByIdAsync(id);

        if (item == null)
            throw new NotFoundException($"Inventory item with id {id} not found");

        item.IsActive = false;
        item.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Inventory.Update(item);
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    /// <summary>
    /// Adds a movement and recalculates current stock.
    /// </summary>
    public async Task<InventoryMovement> AddMovementAsync(
        int itemId, string type, decimal quantity, string? reason, string? orderId)
    {
        var normalizedType = type.ToLowerInvariant();

        if (!ValidMovementTypes.Contains(normalizedType))
            throw new ValidationException("Movement type must be 'in', 'out', or 'adjustment'");

        var item = await _unitOfWork.Inventory.GetByIdAsync(itemId);

        if (item == null)
            throw new NotFoundException($"Inventory item with id {itemId} not found");

        var movement = new InventoryMovement
        {
            InventoryItemId = itemId,
            Type = normalizedType,
            Quantity = quantity,
            Reason = reason,
            OrderId = orderId,
            CreatedAt = DateTime.UtcNow
        };

        // Recalculate stock
        switch (normalizedType)
        {
            case "in":
                item.CurrentStock += quantity;
                break;
            case "out":
                item.CurrentStock -= quantity;
                break;
            case "adjustment":
                item.CurrentStock = quantity;
                break;
        }

        item.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.InventoryMovements.AddAsync(movement);
        _unitOfWork.Inventory.Update(item);
        await _unitOfWork.SaveChangesAsync();

        return movement;
    }

    /// <summary>
    /// Gets all movements for an inventory item.
    /// </summary>
    public async Task<IEnumerable<InventoryMovement>> GetMovementsAsync(int itemId)
    {
        var item = await _unitOfWork.Inventory.GetWithMovementsAsync(itemId);

        if (item == null)
            throw new NotFoundException($"Inventory item with id {itemId} not found");

        return item.Movements ?? Enumerable.Empty<InventoryMovement>();
    }

    /// <summary>
    /// Gets all consumption rules for a product, including inventory item details.
    /// </summary>
    public async Task<IEnumerable<ProductConsumption>> GetConsumptionByProductAsync(int productId)
    {
        return await _unitOfWork.ProductConsumptions.GetByProductAsync(productId);
    }

    /// <summary>
    /// Creates or updates a product consumption rule.
    /// </summary>
    public async Task<ProductConsumption> CreateConsumptionAsync(int productId, int inventoryItemId, decimal quantityPerSale)
    {
        var existing = await _unitOfWork.ProductConsumptions
            .GetByProductAndItemAsync(productId, inventoryItemId);

        if (existing != null)
        {
            existing.QuantityPerSale = quantityPerSale;
            _unitOfWork.ProductConsumptions.Update(existing);
            await _unitOfWork.SaveChangesAsync();
            return existing;
        }

        var consumption = new ProductConsumption
        {
            ProductId = productId,
            InventoryItemId = inventoryItemId,
            QuantityPerSale = quantityPerSale
        };

        await _unitOfWork.ProductConsumptions.AddAsync(consumption);
        await _unitOfWork.SaveChangesAsync();
        return consumption;
    }

    /// <summary>
    /// Deletes a product consumption rule.
    /// </summary>
    public async Task<bool> DeleteConsumptionAsync(int id)
    {
        var consumption = await _unitOfWork.ProductConsumptions.GetByIdAsync(id);

        if (consumption == null)
            throw new NotFoundException($"Product consumption with id {id} not found");

        _unitOfWork.ProductConsumptions.Delete(consumption);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Deducts inventory based on products sold. Best-effort — never throws.
    /// Supports both TrackStock products (direct) and ProductConsumption (recipe-based).
    /// </summary>
    public async Task DeductFromSaleAsync(string orderId, List<SaleItem> items)
    {
        foreach (var item in items)
        {
            try
            {
                var product = await _unitOfWork.Products.GetByIdAsync(item.ProductId);

                if (product != null && product.TrackStock)
                {
                    // Direct stock tracking (abarrotes/papelería)
                    product.CurrentStock -= item.Quantity;
                    if (product.CurrentStock < 0) product.CurrentStock = 0;

                    if (product.CurrentStock <= product.LowStockThreshold)
                        product.IsAvailable = false;

                    _unitOfWork.Products.Update(product);

                    var movement = new InventoryMovement
                    {
                        ProductId = item.ProductId,
                        Type = "out",
                        Quantity = item.Quantity,
                        Reason = $"Venta orden {orderId}",
                        OrderId = orderId,
                        CreatedAt = DateTime.UtcNow
                    };
                    await _unitOfWork.InventoryMovements.AddAsync(movement);
                }
                else
                {
                    // Recipe-based consumption (restaurante/fonda)
                    var consumptions = await _unitOfWork.ProductConsumptions
                        .GetByProductAsync(item.ProductId);

                    foreach (var consumption in consumptions)
                    {
                        var quantityToDeduct = consumption.QuantityPerSale * item.Quantity;
                        await AddMovementAsync(
                            consumption.InventoryItemId,
                            "out",
                            quantityToDeduct,
                            $"Venta orden {orderId}",
                            orderId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to deduct inventory for ProductId {ProductId} on Order {OrderId}",
                    item.ProductId, orderId);
            }
        }

        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Gets product IDs whose inventory items have zero or negative stock.
    /// </summary>
    public async Task<IEnumerable<int>> GetOutOfStockProductIdsAsync(int branchId)
    {
        var allItems = await _unitOfWork.Inventory.GetAllByBranchAsync(branchId);
        var outOfStockItemIds = allItems
            .Where(i => i.CurrentStock <= 0)
            .Select(i => i.Id)
            .ToList();

        var productIds = new HashSet<int>();
        foreach (var itemId in outOfStockItemIds)
        {
            var consumptions = await _unitOfWork.ProductConsumptions
                .GetAsync(pc => pc.InventoryItemId == itemId);
            foreach (var pc in consumptions)
            {
                productIds.Add(pc.ProductId);
            }
        }

        return productIds;
    }

    /// <summary>
    /// Gets inventory movements for a product with TrackStock enabled, ordered by CreatedAt descending.
    /// </summary>
    public async Task<IEnumerable<InventoryMovement>> GetProductMovementsAsync(int productId)
    {
        var movements = await _unitOfWork.InventoryMovements.GetAsync(
            m => m.ProductId == productId);

        return movements.OrderByDescending(m => m.CreatedAt);
    }

    #endregion
}
