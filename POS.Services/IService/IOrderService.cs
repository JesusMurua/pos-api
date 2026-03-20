using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing orders and offline sync.
/// </summary>
public interface IOrderService
{
    /// <summary>
    /// Syncs a batch of offline orders. Idempotent — skips duplicates by UUID.
    /// Returns a summary with synced, skipped, and failed counts.
    /// </summary>
    Task<SyncResult> SyncOrdersAsync(IEnumerable<SyncOrderRequest> orders);

    /// <summary>
    /// Retrieves orders for a branch on a specific date.
    /// </summary>
    Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateTime date);

    /// <summary>
    /// Retrieves order data for daily KPI summary.
    /// </summary>
    Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateTime date);

    /// <summary>
    /// Gets the last order number for a branch. Returns 0 if no orders exist.
    /// </summary>
    Task<int> GetLastOrderNumberAsync(int branchId);
}

/// <summary>
/// Result of a batch sync operation.
/// </summary>
public class SyncResult
{
    public int Synced { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
