using Microsoft.Extensions.Logging;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Repository.Utils;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Implements inventory management operations including CRUD for ingredients,
/// recipe (ProductConsumption) management, and the typed ledger of movements.
/// </summary>
public class InventoryService : IInventoryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InventoryService> _logger;

    private static readonly string[] ValidLegacyMovementTypes = { "in", "out", "adjustment" };

    public InventoryService(IUnitOfWork unitOfWork, ILogger<InventoryService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    #region Inventory Item CRUD

    /// <inheritdoc/>
    public async Task<IEnumerable<InventoryItem>> GetAllAsync(int branchId)
    {
        return await _unitOfWork.Inventory.GetAllByBranchAsync(branchId);
    }

    /// <inheritdoc/>
    public async Task<InventoryItem> GetByIdAsync(int id)
    {
        var item = await _unitOfWork.Inventory.GetByIdAsync(id);

        if (item == null)
            throw new NotFoundException($"Inventory item with id {id} not found");

        return item;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<InventoryItem>> GetLowStockAsync(int branchId)
    {
        return await _unitOfWork.Inventory.GetLowStockAsync(branchId);
    }

    /// <inheritdoc/>
    public async Task<InventoryItem> CreateAsync(InventoryItem item)
    {
        item.CreatedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;

        // Mirror UnitOfMeasure into the legacy Unit string for backwards compatibility
        item.Unit = item.UnitOfMeasure.ToString();

        await _unitOfWork.Inventory.AddAsync(item);
        await _unitOfWork.SaveChangesAsync();

        return item;
    }

    /// <inheritdoc/>
    public async Task<InventoryItem> UpdateAsync(int id, InventoryItem item)
    {
        var existing = await _unitOfWork.Inventory.GetByIdAsync(id);

        if (existing == null)
            throw new NotFoundException($"Inventory item with id {id} not found");

        existing.Name = item.Name;
        existing.UnitOfMeasure = item.UnitOfMeasure;
        existing.Unit = item.UnitOfMeasure.ToString(); // keep legacy in sync
        existing.LowStockThreshold = item.LowStockThreshold;
        existing.CostCents = item.CostCents;
        existing.IsActive = item.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        _unitOfWork.Inventory.Update(existing);
        await _unitOfWork.SaveChangesAsync();

        return existing;
    }

    /// <inheritdoc/>
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

    #endregion

    #region Legacy Movement

    /// <inheritdoc/>
    public async Task<InventoryMovement> AddMovementAsync(
        int itemId, string type, decimal quantity, string? reason, string? orderId)
    {
        var normalizedType = type.ToLowerInvariant();

        if (!ValidLegacyMovementTypes.Contains(normalizedType))
            throw new ValidationException("Movement type must be 'in', 'out', or 'adjustment'");

        var item = await _unitOfWork.Inventory.GetByIdAsync(itemId);

        if (item == null)
            throw new NotFoundException($"Inventory item with id {itemId} not found");

        switch (normalizedType)
        {
            case "in":
                item.CurrentStock += quantity;
                break;
            case "out":
                item.CurrentStock -= quantity;
                break;
            case "adjustment":
                // Legacy "adjustment" overwrites — preserved as-is for backwards compatibility
                item.CurrentStock = quantity;
                break;
        }

        item.UpdatedAt = DateTime.UtcNow;

        var movement = new InventoryMovement
        {
            InventoryItemId = itemId,
            TransactionType = MapLegacyTypeToTransactionType(normalizedType),
            Type = normalizedType,
            Quantity = normalizedType == "adjustment" ? Math.Abs(quantity) : Math.Abs(quantity),
            StockAfterTransaction = item.CurrentStock,
            Reason = reason,
            OrderId = orderId,
            CreatedBy = "legacy-api",
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.InventoryMovements.AddAsync(movement);
        _unitOfWork.Inventory.Update(item);
        await _unitOfWork.SaveChangesAsync();

        return movement;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<InventoryMovement>> GetMovementsAsync(int itemId)
    {
        var item = await _unitOfWork.Inventory.GetWithMovementsAsync(itemId);

        if (item == null)
            throw new NotFoundException($"Inventory item with id {itemId} not found");

        return item.Movements ?? Enumerable.Empty<InventoryMovement>();
    }

    #endregion

    #region Typed Ledger Operations (Phase 18)

    /// <inheritdoc/>
    public async Task<InventoryMovement> RegisterPurchaseAsync(
        int inventoryItemId,
        decimal quantity,
        int? costCentsPerUnit,
        string? note,
        string createdBy)
    {
        if (quantity <= 0)
            throw new ValidationException("Purchase quantity must be greater than zero.");

        var item = await _unitOfWork.Inventory.GetByIdAsync(inventoryItemId);

        if (item == null)
            throw new NotFoundException($"Inventory item with id {inventoryItemId} not found");

        item.CurrentStock += quantity;

        if (costCentsPerUnit.HasValue && costCentsPerUnit.Value > 0)
            item.CostCents = costCentsPerUnit.Value;

        item.UpdatedAt = DateTime.UtcNow;

        var movement = new InventoryMovement
        {
            InventoryItemId = inventoryItemId,
            TransactionType = InventoryTransactionType.Purchase,
            Type = "in",
            Quantity = quantity,
            StockAfterTransaction = item.CurrentStock,
            Reason = note,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        _unitOfWork.Inventory.Update(item);
        await _unitOfWork.InventoryMovements.AddAsync(movement);
        await _unitOfWork.SaveChangesAsync();

        return movement;
    }

    /// <inheritdoc/>
    public async Task<InventoryMovement> RegisterWasteAsync(
        int inventoryItemId,
        decimal quantity,
        string reason,
        string createdBy)
    {
        if (quantity <= 0)
            throw new ValidationException("Waste quantity must be greater than zero.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationException("A reason is required when registering waste.");

        var item = await _unitOfWork.Inventory.GetByIdAsync(inventoryItemId);

        if (item == null)
            throw new NotFoundException($"Inventory item with id {inventoryItemId} not found");

        item.CurrentStock -= quantity;
        item.UpdatedAt = DateTime.UtcNow;

        var movement = new InventoryMovement
        {
            InventoryItemId = inventoryItemId,
            TransactionType = InventoryTransactionType.Waste,
            Type = "out",
            Quantity = quantity,
            StockAfterTransaction = item.CurrentStock,
            Reason = reason,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        _unitOfWork.Inventory.Update(item);
        await _unitOfWork.InventoryMovements.AddAsync(movement);
        await _unitOfWork.SaveChangesAsync();

        return movement;
    }

    /// <inheritdoc/>
    public async Task<InventoryMovement> RegisterManualAdjustmentAsync(
        int inventoryItemId,
        decimal delta,
        string reason,
        string createdBy)
    {
        if (delta == 0)
            throw new ValidationException("Adjustment delta cannot be zero.");

        if (string.IsNullOrWhiteSpace(reason))
            throw new ValidationException("A reason is required for a manual adjustment.");

        var item = await _unitOfWork.Inventory.GetByIdAsync(inventoryItemId);

        if (item == null)
            throw new NotFoundException($"Inventory item with id {inventoryItemId} not found");

        item.CurrentStock += delta;
        item.UpdatedAt = DateTime.UtcNow;

        var movement = new InventoryMovement
        {
            InventoryItemId = inventoryItemId,
            TransactionType = InventoryTransactionType.ManualAdjustment,
            Type = delta >= 0 ? "in" : "out",
            Quantity = Math.Abs(delta),
            StockAfterTransaction = item.CurrentStock,
            Reason = reason,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow
        };

        _unitOfWork.Inventory.Update(item);
        await _unitOfWork.InventoryMovements.AddAsync(movement);
        await _unitOfWork.SaveChangesAsync();

        return movement;
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<InventoryMovement>> GetMovementHistoryAsync(
        int branchId,
        int? inventoryItemId,
        InventoryTransactionType? type,
        DateTime? from,
        DateTime? to)
    {
        return await _unitOfWork.InventoryMovements.GetHistoryAsync(
            branchId, inventoryItemId, type, from, to);
    }

    /// <inheritdoc/>
    public async Task<PageData<InventoryLedgerDto>> GetLedgerAsync(int branchId, PageFilter filter)
    {
        return await _unitOfWork.InventoryMovements.GetLedgerPagedAsync(branchId, filter);
    }

    #endregion

    #region Recipe (ProductConsumption)

    /// <inheritdoc/>
    public async Task<IEnumerable<ProductConsumption>> GetConsumptionByProductAsync(int productId)
    {
        return await _unitOfWork.ProductConsumptions.GetByProductAsync(productId);
    }

    /// <inheritdoc/>
    public async Task<ProductConsumption> CreateConsumptionAsync(
        int productId, int inventoryItemId, decimal quantityPerSale)
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

    /// <inheritdoc/>
    public async Task<bool> DeleteConsumptionAsync(int id)
    {
        var consumption = await _unitOfWork.ProductConsumptions.GetByIdAsync(id);

        if (consumption == null)
            throw new NotFoundException($"Product consumption with id {id} not found");

        _unitOfWork.ProductConsumptions.Delete(consumption);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    #endregion

    #region Sync Engine Integration

    /// <inheritdoc/>
    public async Task DeductFromSaleAsync(string orderId, List<SaleItem> items)
    {
        try
        {
            var orderItems = items
                .Select(i => (OrderId: orderId, i.ProductId, Quantity: (decimal)i.Quantity))
                .ToList();

            await DeductBatchCoreAsync(orderItems);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deduct inventory for Order {OrderId}", orderId);
        }
    }

    /// <inheritdoc/>
    public async Task DeductFromOrdersBatchAsync(List<Order> orders)
    {
        try
        {
            var orderItems = orders
                .Where(o => o.Items != null)
                .SelectMany(o => o.Items!.Select(i =>
                    (OrderId: o.Id, i.ProductId, Quantity: (decimal)i.Quantity)))
                .ToList();

            if (orderItems.Count == 0) return;

            await DeductBatchCoreAsync(orderItems);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to batch deduct inventory for {Count} orders", orders.Count);
        }
    }

    #endregion

    #region Utilities

    /// <inheritdoc/>
    public async Task<IEnumerable<int>> GetOutOfStockProductIdsAsync(int branchId)
    {
        var allItems = await _unitOfWork.Inventory.GetAllByBranchAsync(branchId);
        var outOfStockItemIds = allItems
            .Where(i => i.CurrentStock <= 0)
            .Select(i => i.Id)
            .ToList();

        if (outOfStockItemIds.Count == 0)
            return Enumerable.Empty<int>();

        var consumptions = await _unitOfWork.ProductConsumptions
            .GetAsync(pc => outOfStockItemIds.Contains(pc.InventoryItemId));

        return consumptions.Select(pc => pc.ProductId).Distinct();
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<InventoryMovement>> GetProductMovementsAsync(int productId)
    {
        var movements = await _unitOfWork.InventoryMovements.GetAsync(
            m => m.ProductId == productId);

        return movements.OrderByDescending(m => m.CreatedAt);
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Core batch deduction logic. Zero DB queries inside loops.
    /// <list type="number">
    ///   <item>Batch-fetches all products referenced in the sale.</item>
    ///   <item>Separates TrackStock (direct) from recipe-based products.</item>
    ///   <item>Batch-fetches ProductConsumptions and InventoryItems.</item>
    ///   <item>Calculates all deductions in memory.</item>
    ///   <item>Bulk-inserts movements with typed ledger fields + single SaveChangesAsync.</item>
    /// </list>
    /// </summary>
    private async Task DeductBatchCoreAsync(
        List<(string OrderId, int ProductId, decimal Quantity)> orderItems)
    {
        // 1. Batch fetch all products referenced in the sale
        var productIds = orderItems.Select(i => i.ProductId).Distinct().ToList();
        var products = (await _unitOfWork.Products.GetAsync(p => productIds.Contains(p.Id)))
            .ToDictionary(p => p.Id);

        // 2. Separate TrackStock (direct deduction) vs. recipe-based products
        var trackStockProductIds = products.Values
            .Where(p => p.TrackStock)
            .Select(p => p.Id)
            .ToHashSet();

        var recipeProductIds = productIds
            .Where(id => !trackStockProductIds.Contains(id))
            .ToList();

        // 3. Batch fetch all ProductConsumption rules for recipe products (1 query)
        var consumptions = recipeProductIds.Count > 0
            ? (await _unitOfWork.ProductConsumptions
                .GetAsync(pc => recipeProductIds.Contains(pc.ProductId)))
                .ToList()
            : new List<ProductConsumption>();

        // 4. Batch fetch all InventoryItems that will be affected (1 query)
        var inventoryItemIds = consumptions
            .Select(c => c.InventoryItemId)
            .Distinct()
            .ToList();

        var inventoryItems = inventoryItemIds.Count > 0
            ? (await _unitOfWork.Inventory.GetAsync(i => inventoryItemIds.Contains(i.Id)))
                .ToDictionary(i => i.Id)
            : new Dictionary<int, InventoryItem>();

        // Group consumptions by ProductId for fast O(1) lookup
        var consumptionsByProduct = consumptions
            .GroupBy(c => c.ProductId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 5. Calculate all deductions in memory — zero additional DB queries
        var movements = new List<InventoryMovement>();

        foreach (var (orderId, productId, quantity) in orderItems)
        {
            if (!products.TryGetValue(productId, out var product))
                continue;

            if (product.TrackStock)
            {
                // ── Path A: Direct stock deduction (TrackStock = true) ──
                product.CurrentStock -= quantity;

                if (product.CurrentStock <= product.LowStockThreshold)
                    product.IsAvailable = false;

                _unitOfWork.Products.Update(product);

                movements.Add(new InventoryMovement
                {
                    ProductId = productId,
                    TransactionType = InventoryTransactionType.ConsumeFromSale,
                    Type = "out",
                    Quantity = quantity,
                    StockAfterTransaction = product.CurrentStock,
                    Reason = $"Venta orden {orderId}",
                    OrderId = orderId,
                    CreatedBy = "SyncEngine",
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                // ── Path B: Recipe-based consumption ──
                if (!consumptionsByProduct.TryGetValue(productId, out var productConsumptions))
                    continue; // No recipe mapped — skip gracefully

                foreach (var consumption in productConsumptions)
                {
                    if (!inventoryItems.TryGetValue(consumption.InventoryItemId, out var invItem))
                        continue;

                    var quantityToDeduct = consumption.QuantityPerSale * quantity;
                    invItem.CurrentStock -= quantityToDeduct;

                    if (invItem.CurrentStock <= invItem.LowStockThreshold)
                        _logger.LogWarning(
                            "Low stock alert: {ItemName} ({ItemId}) stock is {Stock}",
                            invItem.Name, invItem.Id, invItem.CurrentStock);

                    _unitOfWork.Inventory.Update(invItem);

                    movements.Add(new InventoryMovement
                    {
                        InventoryItemId = consumption.InventoryItemId,
                        TransactionType = InventoryTransactionType.ConsumeFromSale,
                        Type = "out",
                        Quantity = quantityToDeduct,
                        StockAfterTransaction = invItem.CurrentStock,
                        Reason = $"Venta orden {orderId}",
                        OrderId = orderId,
                        CreatedBy = "SyncEngine",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }

        // 6. Bulk insert all movements + single SaveChangesAsync
        if (movements.Count > 0)
            await _unitOfWork.InventoryMovements.AddRangeAsync(movements);

        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Maps a legacy type string to the closest <see cref="InventoryTransactionType"/>.
    /// "in" → Purchase, "out" → ConsumeFromSale, "adjustment" → ManualAdjustment.
    /// </summary>
    private static InventoryTransactionType MapLegacyTypeToTransactionType(string type)
    {
        return type switch
        {
            "in" => InventoryTransactionType.Purchase,
            "out" => InventoryTransactionType.ConsumeFromSale,
            "adjustment" => InventoryTransactionType.ManualAdjustment,
            _ => InventoryTransactionType.ManualAdjustment
        };
    }

    #endregion
}
