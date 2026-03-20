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
    public async Task<SyncResult> SyncOrdersAsync(IEnumerable<SyncOrderRequest> orders)
    {
        var result = new SyncResult();

        foreach (var request in orders)
        {
            try
            {
                var existing = await _unitOfWork.Orders.GetAsync(
                    o => o.Id == request.Id);

                if (existing.Any())
                {
                    result.Skipped++;
                    continue;
                }

                var order = MapToOrder(request);
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
                    var failedOrder = MapToOrder(request);
                    failedOrder.SyncStatus = OrderSyncStatus.Failed;
                    failedOrder.SyncedAt = DateTime.UtcNow;

                    await _unitOfWork.Orders.AddAsync(failedOrder);
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

    #region Private Helper Methods

    private static Order MapToOrder(SyncOrderRequest request)
    {
        if (!Enum.TryParse<PaymentMethod>(request.PaymentMethod, true, out var paymentMethod))
            paymentMethod = PaymentMethod.Cash;

        return new Order
        {
            Id = request.Id,
            BranchId = request.BranchId,
            OrderNumber = request.OrderNumber,
            TotalCents = request.TotalCents,
            PaymentMethod = paymentMethod,
            TenderedCents = request.TenderedCents,
            ChangeCents = request.ChangeCents,
            CreatedAt = request.CreatedAt,
            Items = request.Items.Select(i => new OrderItem
            {
                OrderId = request.Id,
                ProductId = i.ProductId,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPriceCents = i.UnitPriceCents,
                SizeName = i.SizeName,
                ExtrasJson = i.ExtrasJson,
                Notes = i.Notes
            }).ToList()
        };
    }

    #endregion
}
