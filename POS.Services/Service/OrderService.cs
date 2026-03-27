using POS.Domain.Enums;
using POS.Domain.Exceptions;
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
                    // Upsert: update totals and replace items
                    var withItems = await _unitOfWork.Orders.GetAsync(
                        o => o.Id == request.Id, "Items");
                    var existingOrder = withItems.First();

                    existingOrder.TotalCents = request.TotalCents;
                    existingOrder.SubtotalCents = request.SubtotalCents;
                    existingOrder.DiscountCents = request.DiscountCents;
                    existingOrder.DiscountLabel = request.DiscountLabel;
                    existingOrder.DiscountReason = request.DiscountReason;
                    existingOrder.TenderedCents = request.TenderedCents;
                    existingOrder.ChangeCents = request.ChangeCents;
                    existingOrder.IsPaid = request.IsPaid;
                    existingOrder.TableId = request.TableId;
                    existingOrder.TableName = request.TableName;
                    existingOrder.SyncedAt = DateTime.UtcNow;

                    if (request.PaymentMethod != null
                        && Enum.TryParse<PaymentMethod>(request.PaymentMethod, true, out var pm))
                        existingOrder.PaymentMethod = pm;

                    // Replace items: clear existing, add new
                    existingOrder.Items?.Clear();
                    foreach (var i in request.Items)
                    {
                        existingOrder.Items ??= new List<OrderItem>();
                        existingOrder.Items.Add(new OrderItem
                        {
                            OrderId = request.Id,
                            ProductId = i.ProductId,
                            ProductName = i.ProductName,
                            Quantity = i.Quantity,
                            UnitPriceCents = i.UnitPriceCents,
                            SizeName = i.SizeName,
                            ExtrasJson = i.ExtrasJson,
                            Notes = i.Notes
                        });
                    }

                    _unitOfWork.Orders.Update(existingOrder);
                    await _unitOfWork.SaveChangesAsync();

                    result.Updated++;
                    continue;
                }

                var order = MapToOrder(request);
                order.SyncStatus = OrderSyncStatus.Synced;
                order.SyncedAt = DateTime.UtcNow;

                await _unitOfWork.Orders.AddAsync(order);
                await _unitOfWork.SaveChangesAsync();

                // Update table status based on order
                if (order.TableId.HasValue)
                {
                    var table = await _unitOfWork.RestaurantTables.GetByIdAsync(order.TableId.Value);
                    if (table != null)
                    {
                        table.Status = order.CancellationReason == null ? "occupied" : "available";
                        _unitOfWork.RestaurantTables.Update(table);
                        await _unitOfWork.SaveChangesAsync();
                    }
                }

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

    /// <summary>
    /// Gets the last order number for a branch. Returns 0 if no orders exist.
    /// </summary>
    public async Task<int> GetLastOrderNumberAsync(int branchId)
    {
        var orders = await _unitOfWork.Orders.GetAsync(
            o => o.BranchId == branchId);

        return orders.Any()
            ? orders.Max(o => o.OrderNumber)
            : 0;
    }

    /// <summary>
    /// Cancels an order by setting cancellation reason, timestamp, and who cancelled it.
    /// </summary>
    public async Task<Order> CancelAsync(string orderId, string reason, string? notes, string cancelledBy)
    {
        var results = await _unitOfWork.Orders.GetAsync(o => o.Id == orderId);
        var order = results.FirstOrDefault();

        if (order == null)
            throw new NotFoundException($"Order with id {orderId} not found");

        if (order.CancelledAt.HasValue)
            throw new ValidationException("Order is already cancelled");

        order.CancellationReason = string.IsNullOrEmpty(notes)
            ? reason
            : $"{reason} — {notes}";
        order.CancelledAt = DateTime.UtcNow;
        order.CancelledBy = cancelledBy;

        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveChangesAsync();
        return order;
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
            SubtotalCents = request.SubtotalCents,
            DiscountCents = request.DiscountCents,
            DiscountLabel = request.DiscountLabel,
            DiscountReason = request.DiscountReason,
            IsPaid = request.IsPaid,
            TableId = request.TableId,
            TableName = request.TableName,
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

    /// <summary>
    /// Gets active (non-cancelled) orders for a specific table.
    /// </summary>
    public async Task<IEnumerable<object>> GetActiveByTableAsync(int tableId)
    {
        var orders = await _unitOfWork.Orders.GetAsync(
            o => o.TableId == tableId
                && o.CancellationReason == null
                && o.IsPaid == false,
            "Items");

        return orders
            .OrderBy(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.TotalCents,
                o.CreatedAt,
                Items = o.Items?.Select(i => new
                {
                    i.ProductName,
                    i.Quantity
                }) ?? Enumerable.Empty<object>()
            });
    }

    #endregion
}
