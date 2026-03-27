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

    /// <summary>
    /// Cancels an order by setting cancellation reason, timestamp, and who cancelled it.
    /// </summary>
    Task<Order> CancelAsync(string orderId, string reason, string? notes, string cancelledBy);

    /// <summary>
    /// Gets active (non-cancelled) orders for a specific table, ordered by CreatedAt ascending.
    /// </summary>
    Task<IEnumerable<object>> GetActiveByTableAsync(int tableId);

    /// <summary>
    /// Updates the kitchen status of an order. Sends push notification when status is "Ready".
    /// </summary>
    Task<Order> UpdateKitchenStatusAsync(string orderId, string status);
}

/// <summary>
/// Result of a batch sync operation.
/// </summary>
public class SyncResult
{
    public int Synced { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
}
