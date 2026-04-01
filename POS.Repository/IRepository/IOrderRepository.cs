using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IOrderRepository : IGenericRepository<Order>
{
    Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateTime date);

    Task<IEnumerable<Order>> GetPendingSyncAsync();

    Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateTime date);

    /// <summary>
    /// Returns orders updated since a timestamp with Items and Payments included.
    /// </summary>
    Task<IEnumerable<Order>> GetPullOrdersAsync(int branchId, DateTime since);

    /// <summary>
    /// Gets all active delivery orders for a branch (not PickedUp or Rejected).
    /// </summary>
    Task<IEnumerable<Order>> GetActiveDeliveryOrdersAsync(int branchId);

    /// <summary>
    /// Gets a single order by its external platform ID.
    /// </summary>
    Task<Order?> GetByExternalIdAsync(int branchId, string externalOrderId);
}
