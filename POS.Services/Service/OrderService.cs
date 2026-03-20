using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class OrderService : IOrderService
{
    private readonly IUnitOfWork _unitOfWork;

    public OrderService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Syncs a batch of offline orders. Processes each order individually.
    /// Skips duplicates by UUID, marks failures, never aborts the batch.
    /// </summary>
    public async Task<SyncResult> SyncOrdersAsync(IEnumerable<Order> orders)
    {
        var result = new SyncResult();

        foreach (var order in orders)
        {
            try
            {
                var existing = await _unitOfWork.Orders.GetAsync(
                    o => o.Id == order.Id);

                if (existing.Any())
                {
                    result.Skipped++;
                    continue;
                }

                order.SyncStatus = OrderSyncStatus.Synced;
                order.SyncedAt = DateTime.UtcNow;

                await _unitOfWork.Orders.AddAsync(order);
                await _unitOfWork.SaveChangesAsync();
                result.Synced++;
            }
            catch
            {
                try
                {
                    order.SyncStatus = OrderSyncStatus.Failed;
                    order.SyncedAt = DateTime.UtcNow;

                    await _unitOfWork.Orders.AddAsync(order);
                    await _unitOfWork.SaveChangesAsync();
                }
                catch
                {
                    // Order could not be persisted at all
                }

                result.Failed++;
            }
        }

        return result;
    }

    /// <summary>
    /// Retrieves orders for a branch on a specific date.
    /// </summary>
    public async Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateTime date)
    {
        return await _unitOfWork.Orders.GetByBranchAndDateAsync(branchId, date);
    }

    /// <summary>
    /// Retrieves order data for daily KPI summary.
    /// </summary>
    public async Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateTime date)
    {
        return await _unitOfWork.Orders.GetDailySummaryAsync(branchId, date);
    }

    #endregion
}
