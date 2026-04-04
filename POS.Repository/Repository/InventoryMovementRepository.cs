using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository.IRepository;

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
}
