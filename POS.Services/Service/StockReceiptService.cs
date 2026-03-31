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

                // Process inventory movement
                if (itemReq.InventoryItemId.HasValue)
                {
                    await ProcessInventoryItemReceiptAsync(itemReq.InventoryItemId.Value, itemReq.Quantity, itemReq.CostCents);
                }
                else if (itemReq.ProductId.HasValue)
                {
                    await ProcessProductReceiptAsync(itemReq.ProductId.Value, itemReq.Quantity);
                }
            }

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

    #region Private Helper Methods

    private async Task ProcessInventoryItemReceiptAsync(int inventoryItemId, decimal quantity, int costCents)
    {
        var item = await _unitOfWork.Inventory.GetByIdAsync(inventoryItemId);

        if (item == null)
        {
            _logger.LogWarning("InventoryItem {Id} not found during stock receipt", inventoryItemId);
            return;
        }

        item.CurrentStock += quantity;
        item.CostCents = costCents;
        item.UpdatedAt = DateTime.UtcNow;
        _unitOfWork.Inventory.Update(item);

        var movement = new InventoryMovement
        {
            InventoryItemId = inventoryItemId,
            Type = "in",
            Quantity = quantity,
            Reason = "Recepción de mercancía",
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.InventoryMovements.AddAsync(movement);
    }

    private async Task ProcessProductReceiptAsync(int productId, decimal quantity)
    {
        var product = await _unitOfWork.Products.GetByIdAsync(productId);

        if (product == null)
        {
            _logger.LogWarning("Product {Id} not found during stock receipt", productId);
            return;
        }

        product.CurrentStock += quantity;

        if (product.CurrentStock > product.LowStockThreshold)
            product.IsAvailable = true;

        _unitOfWork.Products.Update(product);

        var movement = new InventoryMovement
        {
            ProductId = productId,
            Type = "in",
            Quantity = quantity,
            Reason = "Recepción de mercancía",
            CreatedAt = DateTime.UtcNow
        };
        await _unitOfWork.InventoryMovements.AddAsync(movement);
    }

    #endregion
}
