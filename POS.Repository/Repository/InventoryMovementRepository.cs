using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository.IRepository;
using POS.Repository.Utils;

namespace POS.Repository.Repository;

public class InventoryMovementRepository : GenericRepository<InventoryMovement>, IInventoryMovementRepository
{
    public InventoryMovementRepository(ApplicationDbContext context) : base(context)
    {
    }

    /// <inheritdoc/>
    public async Task<IEnumerable<InventoryMovement>> GetHistoryAsync(
        int branchId,
        int? inventoryItemId,
        InventoryTransactionType? type,
        DateTime? from,
        DateTime? to)
    {
        // Only movements that have an InventoryItem (ingredient path).
        // TrackStock direct-path movements (ProductId only) are excluded;
        // those are retrieved via GetProductMovementsAsync.
        var query = _context.InventoryMovements
            .Include(m => m.InventoryItem)
            .Where(m => m.InventoryItemId != null
                        && m.InventoryItem!.BranchId == branchId);

        if (inventoryItemId.HasValue)
            query = query.Where(m => m.InventoryItemId == inventoryItemId.Value);

        if (type.HasValue)
            query = query.Where(m => m.TransactionType == type.Value);

        if (from.HasValue)
            query = query.Where(m => m.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(m => m.CreatedAt <= to.Value);

        return await query
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<PageData<InventoryLedgerDto>> GetLedgerPagedAsync(int branchId, PageFilter filter)
    {
        var query = _context.InventoryMovements
            .AsNoTracking()
            .Where(m => m.InventoryItemId != null
                        && m.InventoryItem!.BranchId == branchId);

        var totalRows = await query.CountAsync();
        var totalPages = (int)Math.Ceiling(totalRows / (double)filter.PageSize);

        var data = await query
            .OrderByDescending(m => m.CreatedAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(m => new InventoryLedgerDto
            {
                Id = m.Id,
                InventoryItemId = m.InventoryItemId,
                ItemName = m.InventoryItem!.Name,
                TransactionType = m.TransactionType,
                Quantity = m.Quantity,
                StockAfterTransaction = m.StockAfterTransaction,
                Reason = m.Reason,
                OrderId = m.OrderId,
                CreatedBy = m.CreatedBy,
                CreatedAt = m.CreatedAt
            })
            .ToListAsync();

        return new PageData<InventoryLedgerDto>
        {
            Data = data,
            RowsCount = totalRows,
            TotalPages = totalPages,
            CurrentPage = filter.Page
        };
    }
}
