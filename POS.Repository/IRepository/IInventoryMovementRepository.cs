using POS.Domain.Enums;
using POS.Domain.Models;

namespace POS.Repository.IRepository;

/// <summary>
/// Repository for <see cref="InventoryMovement"/> ledger entries.
/// Movements are immutable — no Update or Delete methods are exposed.
/// </summary>
public interface IInventoryMovementRepository : IGenericRepository<InventoryMovement>
{
    /// <summary>
    /// Returns inventory movements for a branch with optional filters on item, type, and date range.
    /// Only movements linked to an <see cref="InventoryItem"/> (ingredient path) are included.
    /// Results are ordered by <c>CreatedAt</c> descending.
    /// </summary>
    /// <param name="branchId">Branch to scope the query to.</param>
    /// <param name="inventoryItemId">When provided, returns movements for that specific item only.</param>
    /// <param name="type">When provided, filters by transaction type.</param>
    /// <param name="from">Lower bound for <c>CreatedAt</c> (inclusive). UTC.</param>
    /// <param name="to">Upper bound for <c>CreatedAt</c> (inclusive). UTC.</param>
    Task<IEnumerable<InventoryMovement>> GetHistoryAsync(
        int branchId,
        int? inventoryItemId,
        InventoryTransactionType? type,
        DateTime? from,
        DateTime? to);
}
