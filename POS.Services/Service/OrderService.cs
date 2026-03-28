using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class OrderService : IOrderService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPushNotificationService _pushService;
    private readonly IPromotionService _promotionService;
    private readonly ApplicationDbContext _context;

    public OrderService(
        IUnitOfWork unitOfWork,
        IPushNotificationService pushService,
        IPromotionService promotionService,
        ApplicationDbContext context)
    {
        _unitOfWork = unitOfWork;
        _pushService = pushService;
        _promotionService = promotionService;
        _context = context;
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
                    var withRelations = await _unitOfWork.Orders.GetAsync(
                        o => o.Id == request.Id, "Items,Payments");
                    var existingOrder = withRelations.First();

                    existingOrder.TotalCents = request.TotalCents;
                    existingOrder.SubtotalCents = request.SubtotalCents;
                    existingOrder.OrderDiscountCents = request.OrderDiscountCents;
                    existingOrder.TotalDiscountCents = request.TotalDiscountCents;
                    existingOrder.OrderPromotionId = request.OrderPromotionId;
                    existingOrder.OrderPromotionName = request.OrderPromotionName;
                    existingOrder.IsPaid = request.IsPaid;
                    existingOrder.TableId = request.TableId;
                    existingOrder.TableName = request.TableName;
                    existingOrder.SyncedAt = DateTime.UtcNow;

                    // Replace items
                    existingOrder.Items?.Clear();
                    foreach (var i in request.Items)
                    {
                        existingOrder.Items ??= new List<OrderItem>();
                        existingOrder.Items.Add(MapToOrderItem(request.Id, i));
                    }

                    // Replace payments
                    existingOrder.Payments.Clear();
                    foreach (var p in request.Payments)
                        existingOrder.Payments.Add(MapToPayment(request.Id, p));

                    RecalculatePaymentTotals(existingOrder);

                    _unitOfWork.Orders.Update(existingOrder);
                    await _unitOfWork.SaveChangesAsync();

                    result.Updated++;
                    continue;
                }

                var order = MapToOrder(request);
                order.SyncStatus = OrderSyncStatus.Synced;
                order.SyncedAt = DateTime.UtcNow;

                RecalculateTotals(order);
                RecalculatePaymentTotals(order);

                await _unitOfWork.Orders.AddAsync(order);
                await _unitOfWork.SaveChangesAsync();

                await RecordPromotionUsagesAsync(order);

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

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var tableInfo = order.TableName != null ? $" - Mesa {order.TableName}" : "";
                        await _pushService.SendToBranchAsync(
                            order.BranchId,
                            "Nueva orden 🛎️",
                            $"Orden #{order.OrderNumber}{tableInfo}",
                            new { orderId = order.Id, orderNumber = order.OrderNumber, tableId = order.TableId, tableName = order.TableName });
                    }
                    catch { /* best-effort */ }
                });

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
                catch { }

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
        var orders = await _unitOfWork.Orders.GetAsync(o => o.BranchId == branchId);
        return orders.Any() ? orders.Max(o => o.OrderNumber) : 0;
    }

    /// <summary>
    /// Cancels an order by setting cancellation reason, timestamp, and who cancelled it.
    /// </summary>
    public async Task<Order> CancelAsync(string orderId, string reason, string? notes, string cancelledBy)
    {
        var results = await _unitOfWork.Orders.GetAsync(o => o.Id == orderId);
        var order = results.FirstOrDefault()
            ?? throw new NotFoundException($"Order with id {orderId} not found");

        if (order.CancelledAt.HasValue)
            throw new ValidationException("Order is already cancelled");

        order.CancellationReason = string.IsNullOrEmpty(notes) ? reason : $"{reason} — {notes}";
        order.CancelledAt = DateTime.UtcNow;
        order.CancelledBy = cancelledBy;

        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Updates kitchen status and sends push when status is "Ready".
    /// </summary>
    public async Task<Order> UpdateKitchenStatusAsync(string orderId, string status)
    {
        var validStatuses = new[] { "Pending", "Preparing", "Ready", "Delivered" };
        if (!validStatuses.Contains(status))
            throw new ValidationException($"Invalid kitchen status: {status}");

        var results = await _unitOfWork.Orders.GetAsync(o => o.Id == orderId);
        var order = results.FirstOrDefault()
            ?? throw new NotFoundException($"Order with id {orderId} not found");

        order.KitchenStatus = status;
        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveChangesAsync();

        if (status == "Ready")
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var tableInfo = order.TableName != null ? $" - Mesa {order.TableName}" : "";
                    await _pushService.SendToBranchAsync(
                        order.BranchId,
                        "Orden lista 🍽️",
                        $"Orden #{order.OrderNumber}{tableInfo}",
                        new { orderId = order.Id, orderNumber = order.OrderNumber, tableId = order.TableId, tableName = order.TableName });
                }
                catch { /* best-effort */ }
            });
        }

        return order;
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
                Items = o.Items?.Select(i => new { i.Id, i.ProductName, i.Quantity, i.UnitPriceCents })
                    ?? Enumerable.Empty<object>()
            });
    }

    /// <summary>
    /// Adds a payment to an existing order and recalculates totals.
    /// </summary>
    public async Task<OrderPayment> AddPaymentAsync(string orderId, int branchId, OrderPayment payment)
    {
        var results = await _unitOfWork.Orders.GetAsync(o => o.Id == orderId, "Payments");
        var order = results.FirstOrDefault()
            ?? throw new NotFoundException($"Order with id {orderId} not found");

        if (order.BranchId != branchId)
            throw new UnauthorizedException("Order does not belong to this branch");

        payment.OrderId = orderId;
        payment.CreatedAt = DateTime.UtcNow;
        order.Payments.Add(payment);

        RecalculatePaymentTotals(order);

        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveChangesAsync();

        return payment;
    }

    /// <summary>
    /// Removes a payment from an order and recalculates totals.
    /// </summary>
    public async Task RemovePaymentAsync(string orderId, int paymentId, int branchId)
    {
        var results = await _unitOfWork.Orders.GetAsync(o => o.Id == orderId, "Payments");
        var order = results.FirstOrDefault()
            ?? throw new NotFoundException($"Order with id {orderId} not found");

        if (order.BranchId != branchId)
            throw new UnauthorizedException("Order does not belong to this branch");

        var payment = order.Payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new NotFoundException($"Payment with id {paymentId} not found");

        order.Payments.Remove(payment);
        RecalculatePaymentTotals(order);

        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Gets all payments for an order.
    /// </summary>
    public async Task<IEnumerable<OrderPayment>> GetPaymentsAsync(string orderId, int branchId)
    {
        var results = await _unitOfWork.Orders.GetAsync(o => o.Id == orderId, "Payments");
        var order = results.FirstOrDefault()
            ?? throw new NotFoundException($"Order with id {orderId} not found");

        if (order.BranchId != branchId)
            throw new UnauthorizedException("Order does not belong to this branch");

        return order.Payments;
    }

    /// <summary>
    /// Moves items from one order to another in a single transaction.
    /// </summary>
    public async Task<MoveItemsResult> MoveItemsAsync(string sourceOrderId, string targetOrderId, List<int> itemIds, int branchId)
    {
        if (sourceOrderId == targetOrderId)
            throw new ValidationException("Source and target orders must be different");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var sourceResults = await _unitOfWork.Orders.GetAsync(o => o.Id == sourceOrderId, "Items");
        var source = sourceResults.FirstOrDefault()
            ?? throw new NotFoundException($"Source order {sourceOrderId} not found");

        if (source.BranchId != branchId)
            throw new UnauthorizedException("Source order does not belong to this branch");

        if (source.CancelledAt.HasValue)
            throw new ValidationException("Source order is cancelled");

        var targetResults = await _unitOfWork.Orders.GetAsync(o => o.Id == targetOrderId, "Items");
        var target = targetResults.FirstOrDefault()
            ?? throw new NotFoundException($"Target order {targetOrderId} not found");

        if (target.BranchId != branchId)
            throw new UnauthorizedException("Target order does not belong to this branch");

        if (target.CancelledAt.HasValue)
            throw new ValidationException("Target order is cancelled");

        var itemsToMove = source.Items?
            .Where(i => itemIds.Contains(i.Id))
            .ToList() ?? [];

        if (itemsToMove.Count != itemIds.Count)
            throw new ValidationException("Some items were not found in the source order");

        // Move items
        foreach (var item in itemsToMove)
        {
            source.Items!.Remove(item);
            item.OrderId = targetOrderId;
            target.Items ??= new List<OrderItem>();
            target.Items.Add(item);
        }

        // Recalculate source
        RecalculateOrderTotals(source);

        // Recalculate target
        RecalculateOrderTotals(target);

        var sourceTableFreed = false;

        // If source has no items left, complete it and free table
        if (source.Items == null || source.Items.Count == 0)
        {
            source.KitchenStatus = "Delivered";
            source.IsPaid = true;

            if (source.TableId.HasValue)
            {
                var table = await _unitOfWork.RestaurantTables.GetByIdAsync(source.TableId.Value);
                if (table != null)
                {
                    table.Status = "available";
                    _unitOfWork.RestaurantTables.Update(table);
                    sourceTableFreed = true;
                }
            }
        }

        _unitOfWork.Orders.Update(source);
        _unitOfWork.Orders.Update(target);
        await _unitOfWork.SaveChangesAsync();
        await transaction.CommitAsync();

        return new MoveItemsResult
        {
            SourceOrder = new OrderSummary
            {
                Id = source.Id,
                TotalCents = source.TotalCents,
                ItemCount = source.Items?.Count ?? 0
            },
            TargetOrder = new OrderSummary
            {
                Id = target.Id,
                TotalCents = target.TotalCents,
                ItemCount = target.Items?.Count ?? 0
            },
            SourceTableFreed = sourceTableFreed
        };
    }

    /// <summary>
    /// Merges all items from source order into target order in a single transaction.
    /// </summary>
    public async Task<MergeResult> MergeOrdersAsync(string targetOrderId, string sourceOrderId, int branchId)
    {
        if (sourceOrderId == targetOrderId)
            throw new ValidationException("Source and target orders must be different");

        await using var transaction = await _context.Database.BeginTransactionAsync();

        var sourceResults = await _unitOfWork.Orders.GetAsync(o => o.Id == sourceOrderId, "Items");
        var source = sourceResults.FirstOrDefault()
            ?? throw new NotFoundException($"Source order {sourceOrderId} not found");

        if (source.BranchId != branchId)
            throw new UnauthorizedException("Source order does not belong to this branch");
        if (source.CancelledAt.HasValue)
            throw new ValidationException("Source order is cancelled");

        var targetResults = await _unitOfWork.Orders.GetAsync(o => o.Id == targetOrderId, "Items");
        var target = targetResults.FirstOrDefault()
            ?? throw new NotFoundException($"Target order {targetOrderId} not found");

        if (target.BranchId != branchId)
            throw new UnauthorizedException("Target order does not belong to this branch");
        if (target.CancelledAt.HasValue)
            throw new ValidationException("Target order is cancelled");

        // Move all items from source to target
        var itemsToMove = source.Items?.ToList() ?? [];
        foreach (var item in itemsToMove)
        {
            source.Items!.Remove(item);
            item.OrderId = targetOrderId;
            target.Items ??= new List<OrderItem>();
            target.Items.Add(item);
        }

        // Recalculate target
        RecalculateOrderTotals(target);

        // Close source
        source.SubtotalCents = 0;
        source.TotalCents = 0;
        source.TotalDiscountCents = source.OrderDiscountCents;
        source.KitchenStatus = "Delivered";
        source.IsPaid = true;

        // Free source table
        string? sourceTableName = null;
        var sourceTableFreed = false;
        if (source.TableId.HasValue)
        {
            var table = await _unitOfWork.RestaurantTables.GetByIdAsync(source.TableId.Value);
            if (table != null)
            {
                sourceTableName = table.Name;
                table.Status = "available";
                _unitOfWork.RestaurantTables.Update(table);
                sourceTableFreed = true;
            }
        }

        _unitOfWork.Orders.Update(source);
        _unitOfWork.Orders.Update(target);
        await _unitOfWork.SaveChangesAsync();
        await transaction.CommitAsync();

        return new MergeResult
        {
            TargetOrder = new OrderSummary
            {
                Id = target.Id,
                TotalCents = target.TotalCents,
                ItemCount = target.Items?.Count ?? 0
            },
            SourceTableFreed = sourceTableFreed,
            SourceTableName = sourceTableName
        };
    }

    #endregion

    #region Private Helper Methods

    private static void RecalculateOrderTotals(Order order)
    {
        if (order.Items == null || order.Items.Count == 0)
        {
            order.SubtotalCents = 0;
            order.TotalDiscountCents = order.OrderDiscountCents;
            order.TotalCents = 0;
            return;
        }

        order.SubtotalCents = order.Items.Sum(i => i.UnitPriceCents * i.Quantity);
        var itemDiscounts = order.Items.Sum(i => i.DiscountCents);
        order.TotalDiscountCents = itemDiscounts + order.OrderDiscountCents;
        order.TotalCents = Math.Max(0, order.SubtotalCents - order.OrderDiscountCents);
    }

    private static Order MapToOrder(SyncOrderRequest request)
    {
        return new Order
        {
            Id = request.Id,
            BranchId = request.BranchId,
            OrderNumber = request.OrderNumber,
            TotalCents = request.TotalCents,
            CreatedAt = request.CreatedAt,
            SubtotalCents = request.SubtotalCents,
            OrderDiscountCents = request.OrderDiscountCents,
            TotalDiscountCents = request.TotalDiscountCents,
            OrderPromotionId = request.OrderPromotionId,
            OrderPromotionName = request.OrderPromotionName,
            IsPaid = request.IsPaid,
            TableId = request.TableId,
            TableName = request.TableName,
            Items = request.Items.Select(i => MapToOrderItem(request.Id, i)).ToList(),
            Payments = request.Payments.Select(p => MapToPayment(request.Id, p)).ToList()
        };
    }

    private static OrderItem MapToOrderItem(string orderId, SyncOrderItemRequest i)
    {
        return new OrderItem
        {
            OrderId = orderId,
            ProductId = i.ProductId,
            ProductName = i.ProductName,
            Quantity = i.Quantity,
            UnitPriceCents = i.UnitPriceCents,
            SizeName = i.SizeName,
            ExtrasJson = i.ExtrasJson,
            Notes = i.Notes,
            DiscountCents = i.DiscountCents,
            PromotionId = i.PromotionId,
            PromotionName = i.PromotionName
        };
    }

    private static OrderPayment MapToPayment(string orderId, SyncPaymentRequest p)
    {
        if (!Enum.TryParse<PaymentMethod>(p.Method, true, out var method))
            method = PaymentMethod.Cash;

        return new OrderPayment
        {
            OrderId = orderId,
            Method = method,
            AmountCents = p.AmountCents,
            Reference = p.Reference,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static void RecalculateTotals(Order order)
    {
        if (order.Items == null) return;

        order.SubtotalCents = order.Items.Sum(i => i.UnitPriceCents * i.Quantity);
        var itemDiscounts = order.Items.Sum(i => i.DiscountCents);
        order.TotalDiscountCents = itemDiscounts + order.OrderDiscountCents;
        order.TotalCents = order.SubtotalCents - order.OrderDiscountCents;
        if (order.TotalCents < 0) order.TotalCents = 0;
    }

    private static void RecalculatePaymentTotals(Order order)
    {
        order.PaidCents = order.Payments.Sum(p => p.AmountCents);
        order.ChangeCents = Math.Max(0, order.PaidCents - order.TotalCents);
    }

    private async Task RecordPromotionUsagesAsync(Order order)
    {
        try
        {
            var recordedIds = new HashSet<int>();

            if (order.Items != null)
            {
                foreach (var item in order.Items.Where(i => i.PromotionId.HasValue))
                {
                    var promoId = item.PromotionId!.Value;
                    var promo = await _unitOfWork.Promotions.GetByIdAsync(promoId);
                    if (promo == null || promo.BranchId != order.BranchId)
                    {
                        item.PromotionId = null;
                        continue;
                    }

                    if (!recordedIds.Contains(promoId))
                    {
                        await _promotionService.RecordUsageAsync(promoId, order.BranchId, order.Id);
                        recordedIds.Add(promoId);
                    }
                }
            }

            if (order.OrderPromotionId.HasValue)
            {
                var promoId = order.OrderPromotionId.Value;
                var promo = await _unitOfWork.Promotions.GetByIdAsync(promoId);
                if (promo == null || promo.BranchId != order.BranchId)
                    order.OrderPromotionId = null;
                else if (!recordedIds.Contains(promoId))
                    await _promotionService.RecordUsageAsync(promoId, order.BranchId, order.Id);
            }

            await _unitOfWork.SaveChangesAsync();
        }
        catch { /* best-effort */ }
    }

    #endregion
}
