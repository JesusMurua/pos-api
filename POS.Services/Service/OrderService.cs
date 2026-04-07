using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class OrderService : IOrderService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPushNotificationService _pushService;
    private readonly IPromotionService _promotionService;
    private readonly IInventoryService _inventoryService;
    private readonly ICustomerService _customerService;

    public OrderService(
        IUnitOfWork unitOfWork,
        IPushNotificationService pushService,
        IPromotionService promotionService,
        IInventoryService inventoryService,
        ICustomerService customerService)
    {
        _unitOfWork = unitOfWork;
        _pushService = pushService;
        _promotionService = promotionService;
        _inventoryService = inventoryService;
        _customerService = customerService;
    }

    #region Public API Methods

    /// <summary>
    /// Syncs a batch of offline orders using bulk operations.
    /// Fetches existing orders in a single query, classifies into inserts/updates,
    /// and persists with a single SaveChangesAsync to prevent N+1 connection exhaustion.
    /// </summary>
    public async Task<SyncResult> SyncOrdersAsync(IEnumerable<SyncOrderRequest> orders, int branchId)
    {
        var result = new SyncResult();
        var requests = orders.ToList();
        if (requests.Count == 0) return result;

        // ── Phase 1: Single query to fetch all existing orders ──
        var requestIds = requests.Select(r => r.Id).ToList();
        var existingOrders = (await _unitOfWork.Orders.GetAsync(
            o => requestIds.Contains(o.Id), "Items,Payments"))
            .ToDictionary(o => o.Id);

        // ── Phase 1b: Validate cash register session ──
        if (requests.Any(r => !r.CashRegisterSessionId.HasValue))
            throw new ValidationException(
                "CASH_SESSION_REQUIRED: Se requiere un turno de caja abierto para procesar órdenes locales.");

        var distinctSessionIds = requests
            .Select(r => r.CashRegisterSessionId!.Value)
            .Distinct()
            .ToList();

        foreach (var sessionId in distinctSessionIds)
        {
            var session = await _unitOfWork.CashRegisterSessions.GetByIdAsync(sessionId);
            if (session == null || session.BranchId != branchId || session.Status != CashRegisterStatus.Open)
                throw new ValidationException(
                    "CASH_SESSION_CLOSED: Se requiere un turno de caja abierto para procesar órdenes locales.");
        }

        // ── Phase 2: Classify into inserts vs updates ──
        var ordersToInsert = new List<Order>();
        var updatedOrdersWithNewTables = new List<Order>();
        var failedRequests = new List<SyncOrderRequest>();

        foreach (var request in requests)
        {
            try
            {
                if (existingOrders.TryGetValue(request.Id, out var existingOrder))
                {
                    var isNewTableAssignment = request.TableId.HasValue && existingOrder.TableId == null;

                    existingOrder.TotalCents = request.TotalCents;
                    existingOrder.SubtotalCents = request.SubtotalCents;
                    existingOrder.OrderDiscountCents = request.OrderDiscountCents;
                    existingOrder.TotalDiscountCents = request.TotalDiscountCents;
                    existingOrder.OrderPromotionId = request.OrderPromotionId;
                    existingOrder.OrderPromotionName = request.OrderPromotionName;
                    existingOrder.IsPaid = request.IsPaid;
                    existingOrder.KitchenStatus = ParseKitchenStatus(request.KitchenStatus);
                    existingOrder.TableId = request.TableId;
                    existingOrder.TableName = request.TableName;
                    existingOrder.CashRegisterSessionId ??= request.CashRegisterSessionId;
                    existingOrder.CustomerId = request.CustomerId;
                    existingOrder.SyncedAt = DateTime.UtcNow;

                    existingOrder.Items?.Clear();
                    foreach (var i in request.Items)
                    {
                        existingOrder.Items ??= new List<OrderItem>();
                        existingOrder.Items.Add(MapToOrderItem(request.Id, i));
                    }

                    existingOrder.Payments.Clear();
                    foreach (var p in request.Payments)
                        existingOrder.Payments.Add(MapToPayment(request.Id, p));

                    RecalculatePaymentTotals(existingOrder);
                    _unitOfWork.Orders.Update(existingOrder);

                    if (isNewTableAssignment)
                        updatedOrdersWithNewTables.Add(existingOrder);

                    result.Updated++;
                }
                else
                {
                    var order = MapToOrder(request);
                    order.SyncStatus = OrderSyncStatus.Synced;
                    order.SyncedAt = DateTime.UtcNow;

                    RecalculateTotals(order);
                    RecalculatePaymentTotals(order);
                    ordersToInsert.Add(order);
                    result.Synced++;
                }
            }
            catch
            {
                failedRequests.Add(request);
                result.Failed++;
            }
        }

        // ── Phase 2a-fiscal: Freeze SAT fields from Product onto OrderItem ──
        // Batch-fetch all products referenced by new orders, then enrich each item
        // with immutable fiscal data (SatProductCode, SatUnitCode, TaxRatePercent, TaxAmountCents).
        if (ordersToInsert.Count > 0)
        {
            var allProductIds = ordersToInsert
                .Where(o => o.Items != null)
                .SelectMany(o => o.Items!)
                .Select(i => i.ProductId)
                .Distinct().ToList();

            var productFiscalMap = (await _unitOfWork.Products.GetAsync(
                p => allProductIds.Contains(p.Id)))
                .ToDictionary(p => p.Id);

            foreach (var order in ordersToInsert)
            {
                if (order.Items == null) continue;
                foreach (var item in order.Items)
                {
                    if (productFiscalMap.TryGetValue(item.ProductId, out var product))
                    {
                        item.SatProductCode = product.SatProductCode;
                        item.SatUnitCode = product.SatUnitCode;
                        item.TaxRatePercent = product.TaxRate;

                        // Calculate tax amount (prices include IVA in Mexico)
                        var taxRate = product.TaxRate ?? 0.16m;
                        if (taxRate > 0)
                        {
                            var lineTotal = item.UnitPriceCents * item.Quantity;
                            item.TaxAmountCents = (int)(lineTotal * taxRate / (1 + taxRate));
                        }
                    }
                }
            }
        }

        // ── Phase 2b: Validate Counter→Restaurant table assignments ──
        if (updatedOrdersWithNewTables.Count > 0)
        {
            var assignTableIds = updatedOrdersWithNewTables
                .Select(o => o.TableId!.Value).Distinct().ToList();

            var tablesToOccupy = (await _unitOfWork.RestaurantTables.GetAsync(
                t => assignTableIds.Contains(t.Id))).ToDictionary(t => t.Id);

            foreach (var order in updatedOrdersWithNewTables)
            {
                if (!tablesToOccupy.TryGetValue(order.TableId!.Value, out var table))
                    continue;

                if (table.BranchId != order.BranchId)
                    throw new ValidationException($"Table '{table.Name}' does not belong to this branch.");

                if (!table.IsActive)
                    throw new ValidationException($"Table '{table.Name}' is not active.");

                if (table.Status == "occupied")
                    throw new ConcurrencyConflictException(
                        $"Table '{table.Name}' is already occupied by another order.");

                table.Status = "occupied";
                _unitOfWork.RestaurantTables.Update(table);
            }
        }

        // ── Phase 2c: Validate StoreCredit / LoyaltyPoints payments ──
        // Collect all orders (inserts + updates) that use customer-backed payment methods.
        // These require a valid CustomerId and sufficient balance.
        var allSyncedOrders = ordersToInsert.Concat(
            existingOrders.Values.Where(o => requests.Any(r => r.Id == o.Id))).ToList();

        var customerPayments = allSyncedOrders
            .Where(o => o.Payments.Any(p =>
                p.Method == PaymentMethod.StoreCredit || p.Method == PaymentMethod.LoyaltyPoints))
            .ToList();

        // Cache loaded customers to avoid re-fetching within same batch
        var customerCache = new Dictionary<int, Customer>();

        foreach (var order in customerPayments)
        {
            if (!order.CustomerId.HasValue)
                throw new ValidationException(
                    $"CUSTOMER_REQUIRED: Order #{order.OrderNumber} uses StoreCredit or LoyaltyPoints but has no CustomerId.");

            if (!customerCache.TryGetValue(order.CustomerId.Value, out var customer))
            {
                customer = await _unitOfWork.Customers.GetByIdAsync(order.CustomerId.Value);
                if (customer == null || !customer.IsActive)
                    throw new ValidationException(
                        $"CUSTOMER_NOT_FOUND: Customer {order.CustomerId.Value} not found or inactive.");
                customerCache[customer.Id] = customer;
            }

            // Validate StoreCredit balance
            var creditPayments = order.Payments.Where(p => p.Method == PaymentMethod.StoreCredit).ToList();
            if (creditPayments.Count > 0)
            {
                var totalCreditUsed = creditPayments.Sum(p => p.AmountCents);
                // Credit limit check: if limit > 0, ensure debt won't exceed it
                var projectedBalance = customer.CreditBalanceCents - totalCreditUsed;
                if (customer.CreditLimitCents > 0 && Math.Abs(projectedBalance) > customer.CreditLimitCents)
                    throw new ValidationException(
                        $"INSUFFICIENT_CREDIT: Customer '{customer.FirstName}' would exceed credit limit.");
            }

            // Validate LoyaltyPoints balance
            var pointsPayments = order.Payments.Where(p => p.Method == PaymentMethod.LoyaltyPoints).ToList();
            if (pointsPayments.Count > 0)
            {
                var business = await _unitOfWork.Business.GetByIdAsync(customer.BusinessId);
                if (business == null || !business.LoyaltyEnabled)
                    throw new ValidationException(
                        $"LOYALTY_DISABLED: Loyalty program is not enabled for this business.");

                // Convert cents to points: AmountCents / PointRedemptionValueCents = points needed
                var totalPointsNeeded = business.PointRedemptionValueCents > 0
                    ? pointsPayments.Sum(p => p.AmountCents) / business.PointRedemptionValueCents
                    : pointsPayments.Sum(p => p.AmountCents);

                if (customer.PointsBalance < totalPointsNeeded)
                    throw new ValidationException(
                        $"INSUFFICIENT_POINTS: Customer '{customer.FirstName}' has {customer.PointsBalance} points, needs {totalPointsNeeded}.");
            }
        }

        // ── Phase 3: Batch persist — single SaveChangesAsync ──
        if (ordersToInsert.Count > 0)
            await _unitOfWork.Orders.AddRangeAsync(ordersToInsert);

        if (updatedOrdersWithNewTables.Count > 0)
        {
            // Explicit transaction: order TableId + table Status must be atomic
            await using var syncTransaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new ConcurrencyConflictException(
                    "A table or order was modified by another user. Please refresh and try again.");
            }
            catch
            {
                result.Synced = 0;
                result.Updated = 0;
                result.Failed = requests.Count;
                return result;
            }
            await syncTransaction.CommitAsync();
        }
        else
        {
            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch
            {
                // DbContext is tainted — tracked entities have stale Added/Modified state.
                // Re-adding Failed orders with the same IDs would throw InvalidOperationException.
                // Abandon this context entirely.
                result.Synced = 0;
                result.Updated = 0;
                result.Failed = requests.Count;
                return result;
            }
        }

        // ── Phase 4: Save failed mapping orders (separate from main batch) ──
        if (failedRequests.Count > 0)
        {
            var failedOrders = new List<Order>();
            foreach (var req in failedRequests)
            {
                try
                {
                    var failedOrder = MapToOrder(req);
                    failedOrder.SyncStatus = OrderSyncStatus.Failed;
                    failedOrder.SyncedAt = DateTime.UtcNow;
                    failedOrders.Add(failedOrder);
                }
                catch { /* can't even map — skip */ }
            }

            if (failedOrders.Count > 0)
            {
                try
                {
                    await _unitOfWork.Orders.AddRangeAsync(failedOrders);
                    await _unitOfWork.SaveChangesAsync();
                }
                catch { /* best-effort */ }
            }
        }

        // ── Phase 5: Batch table status + inlined auto-seat reservations ──
        var newOrdersWithTables = ordersToInsert
            .Where(o => o.TableId.HasValue)
            .ToList();

        if (newOrdersWithTables.Count > 0)
        {
            var tableIds = newOrdersWithTables
                .Select(o => o.TableId!.Value)
                .Distinct()
                .ToList();

            var tables = (await _unitOfWork.RestaurantTables.GetAsync(
                t => tableIds.Contains(t.Id)))
                .ToDictionary(t => t.Id);

            foreach (var order in newOrdersWithTables)
            {
                if (tables.TryGetValue(order.TableId!.Value, out var table))
                {
                    table.Status = order.CancellationReason == null ? "occupied" : "available";
                    _unitOfWork.RestaurantTables.Update(table);
                }
            }

            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var branchIds = newOrdersWithTables.Select(o => o.BranchId).Distinct().ToList();

            var confirmedReservations = (await _unitOfWork.Reservations.GetAsync(
                r => tableIds.Contains(r.TableId!.Value)
                    && branchIds.Contains(r.BranchId)
                    && r.ReservationDate == today
                    && r.Status == ReservationStatus.Confirmed))
                .ToList();

            foreach (var reservation in confirmedReservations)
            {
                reservation.Status = ReservationStatus.Seated;
                _unitOfWork.Reservations.Update(reservation);
            }

            await _unitOfWork.SaveChangesAsync();
        }

        // ── Phase 5b: Auto-seat reservations for Counter→Restaurant transitions ──
        if (updatedOrdersWithNewTables.Count > 0)
        {
            var assignedTableIds = updatedOrdersWithNewTables
                .Select(o => o.TableId!.Value).Distinct().ToList();
            var assignBranchIds = updatedOrdersWithNewTables
                .Select(o => o.BranchId).Distinct().ToList();
            var todayUtc = DateOnly.FromDateTime(DateTime.UtcNow);

            var seatedReservations = (await _unitOfWork.Reservations.GetAsync(
                r => assignedTableIds.Contains(r.TableId!.Value)
                    && assignBranchIds.Contains(r.BranchId)
                    && r.ReservationDate == todayUtc
                    && r.Status == ReservationStatus.Confirmed))
                .ToList();

            if (seatedReservations.Count > 0)
            {
                foreach (var reservation in seatedReservations)
                {
                    reservation.Status = ReservationStatus.Seated;
                    _unitOfWork.Reservations.Update(reservation);
                }

                await _unitOfWork.SaveChangesAsync();
            }
        }

        // ── Phase 5c: Process StoreCredit / LoyaltyPoints via CustomerService ──
        // Delegates balance mutations to CustomerService which creates immutable ledger entries.
        // This runs AFTER Phase 3 persist to ensure orders exist in DB before referencing them.
        if (customerPayments.Count > 0)
        {
            foreach (var order in customerPayments)
            {
                var customerId = order.CustomerId!.Value;

                // Process StoreCredit payments → deduct from CreditBalanceCents
                foreach (var payment in order.Payments.Where(p => p.Method == PaymentMethod.StoreCredit))
                {
                    await _customerService.UseCreditAsync(
                        customerId, payment.AmountCents, order.Id, order.BranchId, "SyncEngine");
                }

                // Process LoyaltyPoints payments → deduct from PointsBalance
                foreach (var payment in order.Payments.Where(p => p.Method == PaymentMethod.LoyaltyPoints))
                {
                    var business = await _unitOfWork.Business.GetByIdAsync(
                        customerCache[customerId].BusinessId);
                    var pointsToRedeem = business != null && business.PointRedemptionValueCents > 0
                        ? payment.AmountCents / business.PointRedemptionValueCents
                        : payment.AmountCents;

                    await _customerService.RedeemPointsAsync(
                        customerId, pointsToRedeem, order.Id, order.BranchId, "SyncEngine");
                }
            }
        }

        // ── Phase 5d: Earn loyalty points for orders with a customer ──
        // Awards points for any paid order linked to a customer, based on Business loyalty config.
        var ordersWithCustomer = ordersToInsert
            .Where(o => o.CustomerId.HasValue && o.IsPaid && o.CancellationReason == null)
            .ToList();

        foreach (var order in ordersWithCustomer)
        {
            await _customerService.EarnPointsAsync(
                order.CustomerId!.Value, order.TotalCents, order.Id, order.BranchId, "SyncEngine");
        }

        // ── Phase 6: Batch inventory deduction ──
        if (ordersToInsert.Count > 0)
            await _inventoryService.DeductFromOrdersBatchAsync(ordersToInsert);

        // ── Phase 7: Batch promotion usage recording ──
        if (ordersToInsert.Count > 0)
        {
            try
            {
                var promoOrderPairs = new List<(int PromotionId, int BranchId, string OrderId)>();

                foreach (var order in ordersToInsert)
                {
                    if (order.Items != null)
                    {
                        foreach (var item in order.Items.Where(i => i.PromotionId.HasValue))
                            promoOrderPairs.Add((item.PromotionId!.Value, order.BranchId, order.Id));
                    }

                    if (order.OrderPromotionId.HasValue)
                        promoOrderPairs.Add((order.OrderPromotionId.Value, order.BranchId, order.Id));
                }

                if (promoOrderPairs.Count > 0)
                {
                    var uniquePromoIds = promoOrderPairs.Select(p => p.PromotionId).Distinct().ToList();
                    var promotions = (await _unitOfWork.Promotions.GetAsync(
                        p => uniquePromoIds.Contains(p.Id)))
                        .ToDictionary(p => p.Id);

                    var usages = new List<PromotionUsage>();
                    var recordedPerOrder = new Dictionary<string, HashSet<int>>();

                    foreach (var (promoId, promoBranchId, orderId) in promoOrderPairs)
                    {
                        if (!promotions.TryGetValue(promoId, out var promo) || promo.BranchId != promoBranchId)
                        {
                            var order = ordersToInsert.First(o => o.Id == orderId);
                            if (order.OrderPromotionId == promoId)
                                order.OrderPromotionId = null;
                            if (order.Items != null)
                            {
                                foreach (var item in order.Items.Where(i => i.PromotionId == promoId))
                                    item.PromotionId = null;
                            }
                            continue;
                        }

                        if (!recordedPerOrder.TryGetValue(orderId, out var recorded))
                        {
                            recorded = new HashSet<int>();
                            recordedPerOrder[orderId] = recorded;
                        }

                        if (recorded.Add(promoId))
                        {
                            usages.Add(new PromotionUsage
                            {
                                PromotionId = promoId,
                                BranchId = promoBranchId,
                                OrderId = orderId,
                                UsedAt = DateTime.UtcNow
                            });
                        }
                    }

                    if (usages.Count > 0)
                        await _unitOfWork.PromotionUsages.AddRangeAsync(usages);

                    await _unitOfWork.SaveChangesAsync();
                }
            }
            catch { /* best-effort */ }
        }

        // ── Phase 8: Generate PrintJobs per destination (best-effort) ──
        // Only new orders are processed; updates to existing orders do not re-print.
        // Products are fetched in a single batch query to resolve PrintingDestination.
        if (ordersToInsert.Count > 0)
        {
            try
            {
                await GeneratePrintJobsAsync(ordersToInsert);
            }
            catch { /* best-effort: printing must never block order persistence */ }
        }

        // ── Phase 9: Push notifications (fire-and-forget, no DB) ──
        foreach (var order in ordersToInsert)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    var tableInfo = order.TableName != null ? $" - Mesa {order.TableName}" : "";
                    await _pushService.SendToBranchAsync(
                        order.BranchId,
                        "Nueva orden 🛎️",
                        $"Orden #{order.OrderNumber}{tableInfo}",
                        new { orderId = order.Id, orderNumber = order.OrderNumber,
                              tableId = order.TableId, tableName = order.TableName });
                }
                catch { /* best-effort */ }
            });
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
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ValidationException("The order was modified by another user. Please refresh and try again.");
        }
        return order;
    }

    /// <summary>
    /// Updates kitchen status and sends push when status is "Ready".
    /// </summary>
    public async Task<Order> UpdateKitchenStatusAsync(string orderId, string status)
    {
        var kitchenStatus = ParseKitchenStatus(status);

        var results = await _unitOfWork.Orders.GetAsync(o => o.Id == orderId);
        var order = results.FirstOrDefault()
            ?? throw new NotFoundException($"Order with id {orderId} not found");

        order.KitchenStatus = kitchenStatus;
        _unitOfWork.Orders.Update(order);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ValidationException("The order was modified by another user. Please refresh and try again.");
        }

        if (kitchenStatus == KitchenStatus.Ready)
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

        if (order.CashRegisterSessionId.HasValue)
        {
            var session = await _unitOfWork.CashRegisterSessions.GetByIdAsync(order.CashRegisterSessionId.Value);
            if (session == null || session.Status != CashRegisterStatus.Open)
                throw new ValidationException(
                    "CASH_SESSION_CLOSED: Cannot process payment: the associated cash register session is closed.");
        }

        if (!PaymentStatus.IsValid(payment.Status))
            throw new ValidationException(
                $"Invalid payment status '{payment.Status}'. Must be one of: completed, pending, failed, refunded.");

        payment.Status = payment.Status.ToLowerInvariant();
        payment.OrderId = orderId;
        payment.CreatedAt = DateTime.UtcNow;
        if (payment.Status == PaymentStatus.Completed && payment.ConfirmedAt == null)
            payment.ConfirmedAt = DateTime.UtcNow;
        order.Payments.Add(payment);

        RecalculatePaymentTotals(order);
        order.IsPaid = order.PaidCents >= order.TotalCents;

        _unitOfWork.Orders.Update(order);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ValidationException("The order was modified by another user. Please refresh and try again.");
        }

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
        order.IsPaid = order.PaidCents >= order.TotalCents;

        _unitOfWork.Orders.Update(order);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ValidationException("The order was modified by another user. Please refresh and try again.");
        }
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
    /// Returns a single order by ID as a DTO, scoped to the given branch.
    /// </summary>
    public async Task<OrderPullDto?> GetByIdAsDtoAsync(string orderId, int branchId)
    {
        var results = await _unitOfWork.Orders.GetAsync(
            o => o.Id == orderId && o.BranchId == branchId,
            "Items,Payments");

        var o = results.FirstOrDefault();
        if (o == null) return null;

        return MapToOrderPullDto(o);
    }

    /// <summary>
    /// Returns orders updated since a given timestamp for bidirectional sync.
    /// </summary>
    public async Task<IEnumerable<OrderPullDto>> GetPullOrdersAsync(int branchId, DateTime? since)
    {
        var cutoff = since ?? DateTime.UtcNow.AddHours(-24);

        var orders = await _unitOfWork.Orders.GetPullOrdersAsync(branchId, cutoff);

        return orders.Select(MapToOrderPullDto);
    }

    /// <summary>
    /// Moves items from one order to another in a single transaction.
    /// </summary>
    public async Task<MoveItemsResult> MoveItemsAsync(string sourceOrderId, string targetOrderId, List<int> itemIds, int branchId)
    {
        if (sourceOrderId == targetOrderId)
            throw new ValidationException("Source and target orders must be different");

        await using var transaction = await _unitOfWork.BeginTransactionAsync();

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

        if (source.CashRegisterSessionId != target.CashRegisterSessionId)
            throw new ValidationException("Cannot move items between orders from different cash register sessions.");

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
            source.KitchenStatus = KitchenStatus.Delivered;
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
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ValidationException("The order was modified by another user. Please refresh and try again.");
        }
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

        await using var transaction = await _unitOfWork.BeginTransactionAsync();

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

        if (source.CashRegisterSessionId != target.CashRegisterSessionId)
            throw new ValidationException("Cannot merge orders from different cash register sessions.");

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
        source.KitchenStatus = KitchenStatus.Delivered;
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
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ValidationException("The order was modified by another user. Please refresh and try again.");
        }
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

    /// <summary>
    /// Splits an order into multiple new orders by item groups.
    /// </summary>
    public async Task<SplitResult> SplitOrderAsync(string orderId, List<SplitGroup> splits, int branchId)
    {
        if (splits.Count < 2)
            throw new ValidationException("At least 2 split groups are required");

        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        var sourceResults = await _unitOfWork.Orders.GetAsync(o => o.Id == orderId, "Items");
        var source = sourceResults.FirstOrDefault()
            ?? throw new NotFoundException($"Order {orderId} not found");

        if (source.BranchId != branchId)
            throw new UnauthorizedException("Order does not belong to this branch");
        if (source.CancelledAt.HasValue)
            throw new ValidationException("Order is cancelled");

        // Validate all items assigned exactly once
        var allRequestedIds = splits.SelectMany(s => s.ItemIds).ToList();
        var sourceItemIds = source.Items?.Select(i => i.Id).ToHashSet() ?? new HashSet<int>();

        if (allRequestedIds.Count != allRequestedIds.Distinct().Count())
            throw new ValidationException("An item cannot appear in more than one split group");

        if (!allRequestedIds.ToHashSet().SetEquals(sourceItemIds))
            throw new ValidationException("All items in the source order must be assigned to exactly one split group");

        var splitSummaries = new List<SplitOrderSummary>();

        foreach (var group in splits)
        {
            var newOrderId = Guid.NewGuid().ToString();

            string? folioNumber = null;
            try
            {
                var (counter, prefix, format) = await _unitOfWork.Branches.IncrementFolioCounterAsync(branchId);
                folioNumber = !string.IsNullOrEmpty(prefix) ? $"{prefix}-{counter:D4}" : counter.ToString("D4");
            }
            catch { /* folio generation is best-effort */ }

            var newOrder = new Order
            {
                Id = newOrderId,
                BranchId = source.BranchId,
                UserId = source.UserId,
                OrderNumber = source.OrderNumber,
                TableId = source.TableId,
                TableName = source.TableName,
                KitchenStatus = source.KitchenStatus,
                CashRegisterSessionId = source.CashRegisterSessionId,
                FolioNumber = folioNumber,
                CreatedAt = DateTime.UtcNow,
                SyncStatus = OrderSyncStatus.Synced,
                SyncedAt = DateTime.UtcNow,
                Items = new List<OrderItem>(),
                Payments = new List<OrderPayment>()
            };

            var itemsForGroup = source.Items!
                .Where(i => group.ItemIds.Contains(i.Id))
                .ToList();

            foreach (var item in itemsForGroup)
            {
                source.Items!.Remove(item);
                item.OrderId = newOrderId;
                newOrder.Items.Add(item);
            }

            RecalculateOrderTotals(newOrder);
            RecalculatePaymentTotals(newOrder);

            await _unitOfWork.Orders.AddAsync(newOrder);

            splitSummaries.Add(new SplitOrderSummary
            {
                Id = newOrderId,
                FolioNumber = folioNumber,
                Label = group.Label,
                TotalCents = newOrder.TotalCents,
                ItemCount = newOrder.Items.Count
            });
        }

        // Cancel source order
        source.CancellationReason = $"Split into {splits.Count} orders";
        source.CancelledAt = DateTime.UtcNow;
        source.CancelledBy = "System";
        source.SubtotalCents = 0;
        source.TotalCents = 0;

        _unitOfWork.Orders.Update(source);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ValidationException("The order was modified by another user. Please refresh and try again.");
        }
        await transaction.CommitAsync();

        return new SplitResult
        {
            SplitOrders = splitSummaries,
            SourceOrderCancelled = true
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
            KitchenStatus = ParseKitchenStatus(request.KitchenStatus),
            TableId = request.TableId,
            TableName = request.TableName,
            CashRegisterSessionId = request.CashRegisterSessionId,
            CustomerId = request.CustomerId,
            Items = request.Items.Select(i => MapToOrderItem(request.Id, i)).ToList(),
            Payments = request.Payments.Select(p => MapToPayment(request.Id, p)).ToList()
        };
    }

    private static OrderPullDto MapToOrderPullDto(Order o)
    {
        return new OrderPullDto
        {
            Id = o.Id,
            BranchId = o.BranchId,
            FolioNumber = o.FolioNumber,
            TableId = o.TableId,
            TableName = o.TableName,
            KitchenStatus = o.KitchenStatus.ToString(),
            IsPaid = o.IsPaid,
            TotalCents = o.TotalCents,
            SubtotalCents = o.SubtotalCents,
            PaidCents = o.PaidCents,
            ChangeCents = o.ChangeCents,
            CreatedAt = o.CreatedAt,
            UpdatedAt = o.UpdatedAt,
            OrderNumber = o.OrderNumber,
            OrderSource = o.OrderSource.ToString(),
            DeliveryStatus = o.DeliveryStatus?.ToString(),
            ExternalOrderId = o.ExternalOrderId,
            DeliveryCustomerName = o.DeliveryCustomerName,
            Items = o.Items?.Select(i => new OrderPullItemDto
            {
                Id = i.Id,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPriceCents = i.UnitPriceCents,
                SizeName = i.SizeName,
                Notes = i.Notes,
                Extras = ParseExtrasNames(i.ExtrasJson)
            }).ToList() ?? new(),
            Payments = o.Payments?.Select(p => new OrderPullPaymentDto
            {
                Method = p.Method.ToString(),
                AmountCents = p.AmountCents,
                PaymentProvider = p.PaymentProvider,
                ExternalTransactionId = p.ExternalTransactionId,
                OperationId = p.OperationId
            }).ToList() ?? new()
        };
    }

    private static List<string> ParseExtrasNames(string? extrasJson)
    {
        if (string.IsNullOrEmpty(extrasJson)) return new();

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(extrasJson);
            return doc.RootElement.EnumerateArray()
                .Select(e => e.TryGetProperty("name", out var n) ? n.GetString() ?? "" :
                             e.TryGetProperty("label", out var l) ? l.GetString() ?? "" : "")
                .Where(n => n.Length > 0)
                .ToList();
        }
        catch
        {
            return new();
        }
    }

    private static KitchenStatus ParseKitchenStatus(string? status)
    {
        if (string.IsNullOrEmpty(status)) return KitchenStatus.Pending;

        return status.ToLowerInvariant() switch
        {
            "pending" => KitchenStatus.Pending,
            "preparing" => KitchenStatus.Pending,
            "ready" => KitchenStatus.Ready,
            "delivered" => KitchenStatus.Delivered,
            _ => KitchenStatus.Pending
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

        if (!PaymentStatus.IsValid(p.Status))
            throw new ValidationException(
                $"Invalid payment status '{p.Status}'. Must be one of: completed, pending, failed, refunded.");

        var status = p.Status.ToLowerInvariant();

        return new OrderPayment
        {
            OrderId = orderId,
            Method = method,
            AmountCents = p.AmountCents,
            Reference = p.Reference,
            PaymentProvider = p.PaymentProvider,
            ExternalTransactionId = p.ExternalTransactionId,
            PaymentMetadata = p.PaymentMetadata,
            OperationId = p.OperationId,
            Status = status,
            ConfirmedAt = status == PaymentStatus.Completed ? DateTime.UtcNow : null,
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

    private async Task AutoSeatReservationAsync(int tableId, int branchId)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var reservations = await _unitOfWork.Reservations.GetAsync(
            r => r.TableId == tableId
                && r.BranchId == branchId
                && r.ReservationDate == today
                && r.Status == ReservationStatus.Confirmed);

        var reservation = reservations.FirstOrDefault();
        if (reservation != null)
        {
            reservation.Status = ReservationStatus.Seated;
            _unitOfWork.Reservations.Update(reservation);
            await _unitOfWork.SaveChangesAsync();
        }
    }

    private static void RecalculatePaymentTotals(Order order)
    {
        order.PaidCents = order.Payments
            .Where(p => p.Status == PaymentStatus.Completed)
            .Sum(p => p.AmountCents);
        order.ChangeCents = Math.Max(0, order.PaidCents - order.TotalCents);
    }

    private async Task RecordPromotionUsagesAsync(Order order)
    {
        try
        {
            // Collect all unique promotion IDs referenced by this order
            var promoIds = new HashSet<int>();

            if (order.Items != null)
            {
                foreach (var item in order.Items.Where(i => i.PromotionId.HasValue))
                    promoIds.Add(item.PromotionId!.Value);
            }

            if (order.OrderPromotionId.HasValue)
                promoIds.Add(order.OrderPromotionId.Value);

            if (promoIds.Count == 0) return;

            // Single batch query for all promotions
            var promotions = (await _unitOfWork.Promotions.GetAsync(
                p => promoIds.Contains(p.Id)))
                .ToDictionary(p => p.Id);

            var recordedIds = new HashSet<int>();

            // Validate item-level promotions and record usages
            if (order.Items != null)
            {
                foreach (var item in order.Items.Where(i => i.PromotionId.HasValue))
                {
                    var promoId = item.PromotionId!.Value;
                    if (!promotions.TryGetValue(promoId, out var promo) || promo.BranchId != order.BranchId)
                    {
                        item.PromotionId = null;
                        continue;
                    }

                    if (recordedIds.Add(promoId))
                        await _promotionService.RecordUsageAsync(promoId, order.BranchId, order.Id);
                }
            }

            // Validate order-level promotion
            if (order.OrderPromotionId.HasValue)
            {
                var promoId = order.OrderPromotionId.Value;
                if (!promotions.TryGetValue(promoId, out var promo) || promo.BranchId != order.BranchId)
                    order.OrderPromotionId = null;
                else if (recordedIds.Add(promoId))
                    await _promotionService.RecordUsageAsync(promoId, order.BranchId, order.Id);
            }

            await _unitOfWork.SaveChangesAsync();
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// For each new order, groups its items by <see cref="PrintingDestination"/> and creates
    /// one <see cref="PrintJob"/> per destination group. Idempotent: skips destinations
    /// that already have an existing Pending or Printed job for the same order.
    /// Products are fetched in a single batch query to avoid N+1 problems.
    /// </summary>
    /// <param name="orders">Newly inserted orders (not yet printed).</param>
    private async Task GeneratePrintJobsAsync(IEnumerable<Order> orders)
    {
        var orderList = orders.ToList();

        // Collect all distinct ProductIds referenced by the new orders.
        var allProductIds = orderList
            .Where(o => o.Items != null)
            .SelectMany(o => o.Items!)
            .Select(i => i.ProductId)
            .Distinct()
            .ToList();

        if (allProductIds.Count == 0) return;

        // Single batch query: only fetch Id + PrintingDestination to keep it lightweight.
        var productDestinations = (await _unitOfWork.Products.GetAsync(
                p => allProductIds.Contains(p.Id)))
            .ToDictionary(p => p.Id, p => p.PrintingDestination);

        var printJobs = new List<PrintJob>();

        foreach (var order in orderList)
        {
            if (order.Items == null || order.Items.Count == 0) continue;

            // Idempotency guard: load existing jobs for this order.
            var existingJobs = (await _unitOfWork.PrintJobs.GetByOrderAsync(order.Id))
                .Select(j => j.Destination)
                .ToHashSet();

            // Group items by destination; items with an unknown ProductId fall back to Kitchen.
            var groups = order.Items
                .GroupBy(i => productDestinations.TryGetValue(i.ProductId, out var dest)
                    ? dest
                    : PrintingDestination.Kitchen);

            foreach (var group in groups)
            {
                // Skip if this destination already has a job (idempotency).
                if (existingJobs.Contains(group.Key)) continue;

                printJobs.Add(new PrintJob
                {
                    OrderId           = order.Id,
                    BranchId          = order.BranchId,
                    Destination       = group.Key,
                    Status            = PrintJobStatus.Pending,
                    RawContent        = GeneratePrintContent(group.Key, group.ToList(), order),
                    StructuredContent = GenerateStructuredContent(group.Key, group.ToList(), order),
                    CreatedAt         = DateTime.UtcNow,
                    AttemptCount      = 0
                });
            }
        }

        if (printJobs.Count == 0) return;

        await _unitOfWork.PrintJobs.AddRangeAsync(printJobs);
        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// Renders a plain-text ticket for a subset of <see cref="OrderItem"/>s
    /// routed to a specific <see cref="PrintingDestination"/>.
    /// Format is human-readable ESC/POS-compatible text (Phase 19).
    /// Future phases may switch to JSON or ZPL without changing the <c>RawContent</c> column.
    /// </summary>
    private static string GeneratePrintContent(
        PrintingDestination destination,
        List<OrderItem> items,
        Order order)
    {
        var areaLabel = destination switch
        {
            PrintingDestination.Kitchen => "COCINA",
            PrintingDestination.Bar     => "BARRA",
            PrintingDestination.Waiters => "MESEROS",
            _                           => destination.ToString().ToUpper()
        };

        var tableInfo = order.TableName != null ? $"Mesa: {order.TableName}" : "Para llevar";
        var time      = DateTime.UtcNow.ToString("HH:mm");
        var separator = new string('─', 32);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine(separator);
        sb.AppendLine($"ORDEN #{order.OrderNumber,-6} [{areaLabel}]");
        sb.AppendLine($"{tableInfo,-20} {time}");
        sb.AppendLine(separator);

        foreach (var item in items)
        {
            sb.AppendLine($"{item.Quantity}x  {item.ProductName}");

            if (!string.IsNullOrWhiteSpace(item.SizeName))
                sb.AppendLine($"      + Tamaño: {item.SizeName}");

            if (!string.IsNullOrWhiteSpace(item.Notes))
                sb.AppendLine($"      * Nota: {item.Notes}");
        }

        sb.AppendLine(separator);
        return sb.ToString();
    }

    /// <summary>
    /// Renders a structured JSON payload for KDS tablet rendering.
    /// Provides machine-readable ticket data so the KDS can display items with
    /// proper formatting, grouping, and visual emphasis — unlike the raw ESC/POS text.
    /// </summary>
    /// <param name="destination">The physical area this ticket targets.</param>
    /// <param name="items">Order items routed to this destination.</param>
    /// <param name="order">The parent order.</param>
    /// <returns>JSON string conforming to the KdsTicket schema.</returns>
    private static string GenerateStructuredContent(
        PrintingDestination destination,
        List<OrderItem> items,
        Order order)
    {
        var kdsItems = items
            .Select(i => new KdsItem(
                Quantity: i.Quantity,
                Name: i.ProductName,
                Size: string.IsNullOrWhiteSpace(i.SizeName) ? null : i.SizeName,
                Extras: ParseExtrasNames(i.ExtrasJson),
                Notes: string.IsNullOrWhiteSpace(i.Notes) ? null : i.Notes))
            .ToList();

        var ticket = new KdsTicket(
            OrderNumber: order.OrderNumber,
            TableLabel: order.TableName ?? "Para llevar",
            Destination: destination.ToString(),
            Items: kdsItems,
            Priority: "normal",
            CreatedAt: DateTime.UtcNow.ToString("O"));

        return JsonSerializer.Serialize(ticket, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    #endregion

    // ── Private DTOs for KDS structured ticket ──

    private sealed record KdsTicket(
        int OrderNumber,
        string TableLabel,
        string Destination,
        IReadOnlyList<KdsItem> Items,
        string Priority,
        string CreatedAt);

    private sealed record KdsItem(
        int Quantity,
        string Name,
        string? Size,
        IReadOnlyList<string> Extras,
        string? Notes);
}
