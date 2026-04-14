using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing orders and offline sync.
/// </summary>
[Route("api/[controller]")]
public class OrdersController : BaseApiController
{
    private readonly IOrderService _orderService;
    private readonly IMercadoPagoService _mercadoPagoService;
    private readonly IClipService _clipService;

    public OrdersController(
        IOrderService orderService,
        IMercadoPagoService mercadoPagoService,
        IClipService clipService)
    {
        _orderService = orderService;
        _mercadoPagoService = mercadoPagoService;
        _clipService = clipService;
    }

    /// <summary>
    /// Syncs a batch of offline orders from the frontend.
    /// Idempotent — duplicate UUIDs are skipped without error.
    /// </summary>
    /// <param name="orders">The list of orders to sync.</param>
    /// <returns>A sync summary with synced, skipped, and failed counts.</returns>
    /// <response code="200">Returns the sync result summary.</response>
    /// <response code="400">If the request body is invalid.</response>
    [HttpPost("sync")]
    [Authorize(Roles = "Owner,Manager,Cashier,Waiter")]
    [ProducesResponseType(typeof(SyncResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Sync([FromBody] List<SyncOrderRequest> orders)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _orderService.SyncOrdersAsync(orders, BranchId);
        return Ok(result);
    }

    /// <summary>
    /// Retrieves orders for the current branch on a specific date.
    /// </summary>
    /// <param name="date">The date to filter by.</param>
    /// <returns>A list of orders.</returns>
    /// <response code="200">Returns the list of orders.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(IEnumerable<Order>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBranchAndDate([FromQuery] DateTime date)
    {
        var orders = await _orderService.GetByBranchAndDateAsync(BranchId, date);
        return Ok(orders);
    }

    /// <summary>
    /// Retrieves daily KPI summary for the current branch.
    /// </summary>
    /// <param name="date">The date to summarize.</param>
    /// <returns>Order data for daily summary.</returns>
    /// <response code="200">Returns the daily summary data.</response>
    [HttpGet("summary")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(IEnumerable<Order>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDailySummary([FromQuery] DateTime date)
    {
        var orders = await _orderService.GetDailySummaryAsync(BranchId, date);
        return Ok(orders);
    }

    /// <summary>
    /// Gets the last order number for the current branch.
    /// </summary>
    /// <returns>The last order number, or 0 if no orders exist.</returns>
    /// <response code="200">Returns the last order number.</response>
    [HttpGet("last-number")]
    [Authorize(Roles = "Owner,Manager,Cashier,Waiter")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLastOrderNumber()
    {
        var lastNumber = await _orderService.GetLastOrderNumberAsync(BranchId);
        return Ok(new { lastOrderNumber = lastNumber });
    }

    /// <summary>
    /// Returns orders updated since a timestamp for bidirectional sync.
    /// </summary>
    /// <param name="since">ISO 8601 timestamp. Null = last 24 hours.</param>
    /// <returns>A list of orders with items and payments.</returns>
    /// <response code="200">Returns the list of orders.</response>
    [HttpGet("pull")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(IEnumerable<OrderPullDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Pull([FromQuery] DateTime? since)
    {
        var orders = await _orderService.GetPullOrdersAsync(BranchId, since);
        return Ok(orders);
    }

    /// <summary>
    /// Returns a single order by ID for the current branch.
    /// </summary>
    /// <param name="id">The order UUID.</param>
    /// <returns>The order with items and payments.</returns>
    /// <response code="200">Returns the order.</response>
    /// <response code="404">If the order is not found in this branch.</response>
    [HttpGet("{id}")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(OrderPullDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(string id)
    {
        var dto = await _orderService.GetByIdAsDtoAsync(id, BranchId);
        if (dto == null) return NotFound();
        return Ok(dto);
    }

    /// <summary>
    /// Gets active (non-cancelled) orders for a specific table.
    /// </summary>
    /// <param name="tableId">The table identifier.</param>
    /// <returns>A list of active orders for the table.</returns>
    /// <response code="200">Returns the list of active orders, or empty array if none.</response>
    [HttpGet("by-table/{tableId}")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen,Waiter")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByTable(int tableId)
    {
        var orders = await _orderService.GetActiveByTableAsync(tableId);
        return Ok(orders);
    }

    /// <summary>
    /// Cancels an order.
    /// </summary>
    /// <param name="id">The order UUID.</param>
    /// <param name="request">Cancellation reason and optional notes.</param>
    /// <returns>The cancelled order.</returns>
    /// <response code="200">Returns the cancelled order.</response>
    /// <response code="400">If the order is already cancelled.</response>
    /// <response code="404">If the order is not found.</response>
    [HttpPatch("{id}/cancel")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(string id, [FromBody] CancelOrderRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userName = User.Identity?.Name ?? "Unknown";
        var order = await _orderService.CancelAsync(id, request.Reason, request.Notes, userName);
        return Ok(order);
    }

    /// <summary>
    /// Updates the kitchen status of an order.
    /// </summary>
    /// <param name="id">The order UUID.</param>
    /// <param name="request">The new kitchen status.</param>
    /// <returns>The updated order.</returns>
    /// <response code="200">Returns the updated order.</response>
    /// <response code="400">If the status value is invalid.</response>
    /// <response code="404">If the order is not found.</response>
    [HttpPatch("{id}/kitchen-status")]
    [Authorize(Roles = "Owner,Manager,Cashier,Kitchen")]
    [ProducesResponseType(typeof(Order), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateKitchenStatus(string id, [FromBody] UpdateKitchenStatusRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var validStatuses = new[] { "Pending", "Ready", "Delivered" };
        if (!validStatuses.Contains(request.Status))
            return BadRequest(new { message = "Status must be 'Pending', 'Ready', or 'Delivered'" });

        var order = await _orderService.UpdateKitchenStatusAsync(id, request.Status);
        return Ok(order);
    }

    /// <summary>
    /// Gets all payments for an order.
    /// </summary>
    /// <param name="id">The order UUID.</param>
    /// <returns>A list of payments.</returns>
    /// <response code="200">Returns the list of payments.</response>
    /// <response code="404">If the order is not found.</response>
    [HttpGet("{id}/payments")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(IEnumerable<OrderPayment>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPayments(string id)
    {
        var payments = await _orderService.GetPaymentsAsync(id, BranchId);
        return Ok(payments);
    }

    /// <summary>
    /// Adds a payment to an existing order.
    /// </summary>
    /// <param name="id">The order UUID.</param>
    /// <param name="request">The payment data.</param>
    /// <returns>Payment totals summary.</returns>
    /// <response code="200">Returns updated payment totals.</response>
    /// <response code="400">If the payment method is invalid.</response>
    /// <response code="404">If the order is not found.</response>
    [HttpPost("{id}/payments")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddPayment(string id, [FromBody] AddPaymentRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        if (!Enum.TryParse<POS.Domain.Enums.PaymentMethod>(request.Method, true, out var method))
            return BadRequest(new { message = "Invalid payment method" });

        var payment = new OrderPayment
        {
            Method = method,
            AmountCents = request.AmountCents,
            Reference = request.Reference,
            PaymentProvider = request.PaymentProvider,
            ExternalTransactionId = request.ExternalTransactionId,
            PaymentMetadata = request.PaymentMetadata,
            OperationId = request.OperationId,
            PaymentStatusId = POS.Domain.Helpers.PaymentStatus.FromString(request.Status)
        };

        await _orderService.AddPaymentAsync(id, BranchId, payment);

        var order = (await _orderService.GetByBranchAndDateAsync(BranchId, DateTime.UtcNow))
            .FirstOrDefault(o => o.Id == id);

        return Ok(new
        {
            paidCents = order?.PaidCents ?? payment.AmountCents,
            changeCents = order?.ChangeCents ?? 0,
            remainingCents = Math.Max(0, (order?.TotalCents ?? 0) - (order?.PaidCents ?? 0))
        });
    }

    /// <summary>
    /// Removes a payment from an order.
    /// </summary>
    /// <param name="id">The order UUID.</param>
    /// <param name="paymentId">The payment identifier.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Payment removed.</response>
    /// <response code="404">If the order or payment is not found.</response>
    [HttpDelete("{id}/payments/{paymentId}")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemovePayment(string id, int paymentId)
    {
        await _orderService.RemovePaymentAsync(id, paymentId, BranchId);
        return Ok(new { success = true });
    }

    /// <summary>
    /// Creates a MercadoPago payment intent for an order.
    /// Registers a pending payment and returns the checkout URL.
    /// </summary>
    /// <param name="id">The order UUID.</param>
    /// <param name="request">The payment intent data.</param>
    /// <returns>External transaction ID and init point URL.</returns>
    /// <response code="200">Returns the payment intent details.</response>
    /// <response code="400">If MercadoPago is not configured or inactive.</response>
    /// <response code="404">If the order is not found.</response>
    [HttpPost("{id}/payments/mercadopago/intent")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(PaymentIntentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateMercadoPagoIntent(string id, [FromBody] CreatePaymentIntentRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _mercadoPagoService.CreatePaymentIntentAsync(BranchId, id, request.AmountCents);
        return Ok(result);
    }

    /// <summary>
    /// Creates a Clip terminal payment intent for an order.
    /// Pushes the payment to the physical terminal and registers a pending payment.
    /// </summary>
    /// <param name="id">The order UUID.</param>
    /// <param name="request">The payment intent data.</param>
    /// <returns>External transaction ID and status.</returns>
    /// <response code="200">Returns the payment intent details.</response>
    /// <response code="400">If Clip is not configured or inactive.</response>
    /// <response code="404">If the order is not found.</response>
    [HttpPost("{id}/payments/clip/intent")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(PaymentIntentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateClipIntent(string id, [FromBody] CreatePaymentIntentRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _clipService.CreatePaymentIntentAsync(BranchId, id, request.AmountCents);
        return Ok(result);
    }

    /// <summary>
    /// Moves items from this order to another order.
    /// </summary>
    /// <param name="id">The source order UUID.</param>
    /// <param name="request">Target order and item IDs to move.</param>
    /// <returns>Summary of both orders after the move.</returns>
    /// <response code="200">Returns updated order summaries.</response>
    /// <response code="400">If validation fails.</response>
    /// <response code="404">If an order is not found.</response>
    [HttpPost("{id}/move-items")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(MoveItemsResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MoveItems(string id, [FromBody] MoveItemsRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _orderService.MoveItemsAsync(id, request.TargetOrderId, request.ItemIds, BranchId);
        return Ok(result);
    }

    /// <summary>
    /// Merges all items from source order into this (target) order.
    /// Closes source order and frees its table.
    /// </summary>
    /// <param name="id">The target order UUID (survives).</param>
    /// <param name="request">The source order to absorb.</param>
    /// <returns>Summary of the merged target order.</returns>
    /// <response code="200">Returns merge result.</response>
    /// <response code="400">If validation fails.</response>
    /// <response code="404">If an order is not found.</response>
    [HttpPost("{id}/merge")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(MergeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Merge(string id, [FromBody] MergeOrderRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _orderService.MergeOrdersAsync(id, request.SourceOrderId, BranchId);
        return Ok(result);
    }

    /// <summary>
    /// Splits an order into multiple new orders by item groups.
    /// </summary>
    /// <param name="id">The source order UUID to split.</param>
    /// <param name="request">The split groups with item IDs and labels.</param>
    /// <returns>Summary of created split orders.</returns>
    /// <response code="200">Returns split result.</response>
    /// <response code="400">If validation fails.</response>
    /// <response code="404">If the order is not found.</response>
    [HttpPost("{id}/split")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(SplitResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Split(string id, [FromBody] SplitOrderRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var result = await _orderService.SplitOrderAsync(id, request.Splits, BranchId);
        return Ok(result);
    }

    /// <summary>
    /// Lists orphaned orders pending manual reconciliation in the current branch.
    /// </summary>
    /// <returns>A list of orphaned orders.</returns>
    /// <response code="200">Returns the list of orphaned orders.</response>
    [HttpGet("orphaned")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<OrphanedOrderDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetOrphaned()
    {
        var orders = await _orderService.GetOrphanedAsync(BranchId);
        return Ok(orders);
    }

    /// <summary>
    /// Reconciles an orphaned order by attaching it to a CashRegisterSession of the same branch.
    /// The target session may be open or closed (closed is the typical case for prior-day rescue).
    /// </summary>
    /// <param name="id">The order UUID.</param>
    /// <param name="request">Target session and optional admin note.</param>
    /// <returns>The reconciled order.</returns>
    /// <response code="200">Returns the reconciled order.</response>
    /// <response code="400">If the order is not orphaned or session belongs to another branch.</response>
    /// <response code="404">If the order or session is not found.</response>
    [HttpPatch("{id}/reconcile")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(OrphanedOrderDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Reconcile(string id, [FromBody] ReconcileOrderRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userName = User.Identity?.Name ?? "Unknown";
        var dto = await _orderService.ReconcileAsync(id, BranchId, request.CashRegisterSessionId, request.Note, userName);
        return Ok(dto);
    }
}

/// <summary>
/// Request body for merging orders.
/// </summary>
public class MergeOrderRequest
{
    public string SourceOrderId { get; set; } = null!;
}

/// <summary>
/// Request body for splitting an order.
/// </summary>
public class SplitOrderRequest
{
    public List<SplitGroup> Splits { get; set; } = new();
}

/// <summary>
/// Request body for moving items between orders.
/// </summary>
public class MoveItemsRequest
{
    public string TargetOrderId { get; set; } = null!;
    public List<int> ItemIds { get; set; } = new();
}

/// <summary>
/// Request body for updating kitchen status.
/// </summary>
public class UpdateKitchenStatusRequest
{
    public string Status { get; set; } = null!;
}

/// <summary>
/// Request body for adding a payment to an order.
/// </summary>
public class AddPaymentRequest
{
    public string Method { get; set; } = null!;
    public int AmountCents { get; set; }

    [System.ComponentModel.DataAnnotations.MaxLength(50)]
    public string? Reference { get; set; }

    /// <summary>External provider name: "Clip", "MercadoPago", or null for manual payments.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(30)]
    public string? PaymentProvider { get; set; }

    /// <summary>Transaction ID from the external provider.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? ExternalTransactionId { get; set; }

    /// <summary>JSON string with provider-specific data.</summary>
    public string? PaymentMetadata { get; set; }

    /// <summary>Internal tracking ID for the terminal operation.</summary>
    [System.ComponentModel.DataAnnotations.MaxLength(100)]
    public string? OperationId { get; set; }

    /// <summary>Payment lifecycle status: "completed", "pending", "failed", "refunded". Required.</summary>
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.MaxLength(20)]
    public string Status { get; set; } = null!;
}

public class CreatePaymentIntentRequest
{
    public int AmountCents { get; set; }
}
