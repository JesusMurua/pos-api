using Microsoft.Extensions.Logging;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Implements stock receipt management operations.
/// </summary>
public class StockReceiptService : IStockReceiptService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<StockReceiptService> _logger;

    public StockReceiptService(IUnitOfWork unitOfWork, ILogger<StockReceiptService> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    #region Public API Methods

    /// <summary>
    /// Gets all stock receipts for a branch with optional filters.
    /// </summary>
    public async Task<IEnumerable<StockReceipt>> GetAllAsync(
        int branchId, int? supplierId, DateTime? from, DateTime? to)
    {
        return await _unitOfWork.StockReceipts.GetAllByBranchAsync(branchId, supplierId, from, to);
    }

    /// <summary>
    /// Gets a stock receipt by its identifier with items, validated against branch.
    /// </summary>
    public async Task<StockReceipt> GetByIdAsync(int id, int branchId)
    {
        var receipt = await _unitOfWork.StockReceipts.GetWithItemsAsync(id);

        if (receipt == null || receipt.BranchId != branchId)
            throw new NotFoundException($"Stock receipt with id {id} not found");

        return receipt;
    }

    /// <summary>
    /// Creates a stock receipt, saves items, and processes inventory movements in a single transaction.
    /// </summary>
    public async Task<StockReceipt> CreateAsync(
        CreateStockReceiptRequest request, int branchId, int userId)
    {
        // Validate each item has at least one target
        foreach (var item in request.Items)
        {
            if (item.InventoryItemId == null && item.ProductId == null)
                throw new ValidationException("Each item must have an InventoryItemId or ProductId");
        }

        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        try
        {
            var receipt = new StockReceipt
            {
                BranchId = branchId,
                SupplierId = request.SupplierId,
                ReceivedByUserId = userId,
                ReceivedAt = DateTime.UtcNow,
                Notes = request.Notes,
                TotalCents = 0,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.StockReceipts.AddAsync(receipt);
            await _unitOfWork.SaveChangesAsync();

            var totalCents = 0;

            // Batch-fetch all referenced inventory items and products (1 query each)
            var inventoryItemIds = request.Items
                .Where(i => i.InventoryItemId.HasValue)
                .Select(i => i.InventoryItemId!.Value)
                .Distinct().ToList();
            var productIds = request.Items
                .Where(i => i.ProductId.HasValue)
                .Select(i => i.ProductId!.Value)
                .Distinct().ToList();

            var inventoryItems = (await _unitOfWork.Inventory.GetAsync(
                i => inventoryItemIds.Contains(i.Id))).ToDictionary(i => i.Id);
            var products = (await _unitOfWork.Products.GetAsync(
                p => productIds.Contains(p.Id))).ToDictionary(p => p.Id);

            var movements = new List<InventoryMovement>();

            foreach (var itemReq in request.Items)
            {
                var itemTotal = (int)(itemReq.Quantity * itemReq.CostCents);

                var receiptItem = new StockReceiptItem
                {
                    StockReceiptId = receipt.Id,
                    InventoryItemId = itemReq.InventoryItemId,
                    ProductId = itemReq.ProductId,
                    Quantity = itemReq.Quantity,
                    CostCents = itemReq.CostCents,
                    TotalCents = itemTotal,
                    Notes = itemReq.Notes
                };

                receipt.Items.Add(receiptItem);
                totalCents += itemTotal;

                // Process inventory movement using pre-fetched entities
                if (itemReq.InventoryItemId.HasValue
                    && inventoryItems.TryGetValue(itemReq.InventoryItemId.Value, out var invItem))
                {
                    invItem.CurrentStock += itemReq.Quantity;
                    invItem.CostCents = itemReq.CostCents;
                    invItem.UpdatedAt = DateTime.UtcNow;
                    _unitOfWork.Inventory.Update(invItem);

                    movements.Add(new InventoryMovement
                    {
                        InventoryItemId = itemReq.InventoryItemId.Value,
                        Type = "in",
                        Quantity = itemReq.Quantity,
                        Reason = "Recepción de mercancía",
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else if (itemReq.ProductId.HasValue
                    && products.TryGetValue(itemReq.ProductId.Value, out var product))
                {
                    product.CurrentStock += itemReq.Quantity;
                    if (product.CurrentStock > product.LowStockThreshold)
                        product.IsAvailable = true;
                    _unitOfWork.Products.Update(product);

                    movements.Add(new InventoryMovement
                    {
                        ProductId = itemReq.ProductId.Value,
                        Type = "in",
                        Quantity = itemReq.Quantity,
                        Reason = "Recepción de mercancía",
                        CreatedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    var targetId = itemReq.InventoryItemId ?? itemReq.ProductId;
                    _logger.LogWarning("Entity {Id} not found during stock receipt", targetId);
                }
            }

            // Batch-insert all movements in a single operation
            await _unitOfWork.InventoryMovements.AddRangeAsync(movements);

            receipt.TotalCents = totalCents;
            _unitOfWork.StockReceipts.Update(receipt);
            await _unitOfWork.SaveChangesAsync();

            await transaction.CommitAsync();

            // Reload with full includes
            return (await _unitOfWork.StockReceipts.GetWithItemsAsync(receipt.Id))!;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    #endregion
}
