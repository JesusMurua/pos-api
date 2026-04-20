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
    /// <param name="orders">The list of orders to sync.</param>
    /// <param name="branchId">The branch identifier from JWT token.</param>
    Task<SyncResult> SyncOrdersAsync(IEnumerable<SyncOrderRequest> orders, int branchId);

    /// <summary>
    /// Retrieves orders for a branch on a given local calendar day. The branch's
    /// persistent <c>TimeZoneId</c> drives the UTC range computation.
    /// </summary>
    Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateOnly localDate);

    /// <summary>
    /// Retrieves order data for daily KPI summary on a given local calendar day.
    /// The branch's persistent <c>TimeZoneId</c> drives the UTC range computation.
    /// </summary>
    Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateOnly localDate);

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

    /// <summary>
    /// Adds a payment to an existing order and recalculates totals.
    /// </summary>
    Task<OrderPayment> AddPaymentAsync(string orderId, int branchId, OrderPayment payment);

    /// <summary>
    /// Removes a payment from an order and recalculates totals.
    /// </summary>
    Task RemovePaymentAsync(string orderId, int paymentId, int branchId);

    /// <summary>
    /// Gets all payments for an order.
    /// </summary>
    Task<IEnumerable<OrderPayment>> GetPaymentsAsync(string orderId, int branchId);

    /// <summary>
    /// Confirms a pending payment by its ExternalTransactionId.
    /// Updates the payment status to "completed", sets ConfirmedAt, and recalculates order totals.
    /// </summary>
    Task<OrderPayment> ConfirmPaymentByExternalIdAsync(string externalTransactionId);

    /// <summary>
    /// Returns a single order by ID as a DTO, scoped to the given branch.
    /// </summary>
    Task<OrderPullDto?> GetByIdAsDtoAsync(string orderId, int branchId);

    /// <summary>
    /// Returns orders updated since a given timestamp for bidirectional sync.
    /// </summary>
    Task<IEnumerable<OrderPullDto>> GetPullOrdersAsync(int branchId, DateTime? since);

    /// <summary>
    /// Moves items from one order to another. Recalculates totals on both.
    /// If source order becomes empty, marks it completed and frees the table.
    /// </summary>
    Task<MoveItemsResult> MoveItemsAsync(string sourceOrderId, string targetOrderId, List<int> itemIds, int branchId);

    /// <summary>
    /// Merges all items from source order into target order.
    /// Closes source order and frees its table.
    /// </summary>
    Task<MergeResult> MergeOrdersAsync(string targetOrderId, string sourceOrderId, int branchId);

    /// <summary>
    /// Splits an order into multiple new orders by item groups.
    /// Cancels the source order after splitting.
    /// </summary>
    Task<SplitResult> SplitOrderAsync(string orderId, List<SplitGroup> splits, int branchId);

    /// <summary>
    /// Returns orders flagged IsOrphaned for a branch, awaiting manual reconciliation.
    /// </summary>
    Task<IEnumerable<OrphanedOrderDto>> GetOrphanedAsync(int branchId);

    /// <summary>
    /// Attaches an orphaned order to an existing CashRegisterSession of the same branch
    /// (open or closed), clears IsOrphaned, and stamps audit fields.
    /// </summary>
    /// <exception cref="NotFoundException">Order or session not found.</exception>
    /// <exception cref="ValidationException">Order is not orphaned, or session belongs to another branch.</exception>
    Task<OrphanedOrderDto> ReconcileAsync(string orderId, int branchId, int targetSessionId, string? note, string reconciledBy);
}

public class MoveItemsResult
{
    public OrderSummary SourceOrder { get; set; } = null!;
    public OrderSummary TargetOrder { get; set; } = null!;
    public bool SourceTableFreed { get; set; }
}

public class OrderSummary
{
    public string Id { get; set; } = null!;
    public int TotalCents { get; set; }
    public int ItemCount { get; set; }
}

public class SplitGroup
{
    public List<int> ItemIds { get; set; } = new();
    public string? Label { get; set; }
}

public class SplitResult
{
    public List<SplitOrderSummary> SplitOrders { get; set; } = new();
    public bool SourceOrderCancelled { get; set; }
}

public class SplitOrderSummary
{
    public string Id { get; set; } = null!;
    public string? FolioNumber { get; set; }
    public string? Label { get; set; }
    public int TotalCents { get; set; }
    public int ItemCount { get; set; }
}

public class MergeResult
{
    public OrderSummary TargetOrder { get; set; } = null!;
    public bool SourceTableFreed { get; set; }
    public string? SourceTableName { get; set; }
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

public class OrderPullDto
{
    public string Id { get; set; } = null!;
    public int BranchId { get; set; }
    public string? FolioNumber { get; set; }
    public int? TableId { get; set; }
    public string? TableName { get; set; }
    public string KitchenStatus { get; set; } = null!;
    public bool IsPaid { get; set; }
    public int TotalCents { get; set; }
    public int SubtotalCents { get; set; }
    public int PaidCents { get; set; }
    public int ChangeCents { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int OrderNumber { get; set; }
    public string OrderSource { get; set; } = "Direct";
    public string? DeliveryStatus { get; set; }
    public string? ExternalOrderId { get; set; }
    public string? DeliveryCustomerName { get; set; }
    public List<OrderPullItemDto> Items { get; set; } = new();
    public List<OrderPullPaymentDto> Payments { get; set; } = new();
}

public class OrderPullItemDto
{
    public int Id { get; set; }
    public string ProductName { get; set; } = null!;
    public int Quantity { get; set; }
    public int UnitPriceCents { get; set; }
    public string? SizeName { get; set; }
    public string? Notes { get; set; }
    public List<string> Extras { get; set; } = new();
}

public class ReconcileOrderRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    public int CashRegisterSessionId { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(500)]
    public string? Note { get; set; }
}

public class OrderPullPaymentDto
{
    public string Method { get; set; } = null!;
    public int AmountCents { get; set; }
    /// <summary>External provider name, or null for manual payments.</summary>
    public string? PaymentProvider { get; set; }
    /// <summary>Transaction ID from the external provider.</summary>
    public string? ExternalTransactionId { get; set; }
    /// <summary>Internal tracking ID for the terminal operation.</summary>
    public string? OperationId { get; set; }
}
