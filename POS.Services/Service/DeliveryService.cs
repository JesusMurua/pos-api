using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class DeliveryService : IDeliveryService
{
    private readonly IUnitOfWork _unitOfWork;

    public DeliveryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Accepts a delivery order that is pending acceptance.
    /// Sets DeliveryStatus to Accepted and KitchenStatus to Pending (appears in KDS).
    /// </summary>
    public async Task<Order> AcceptDeliveryOrderAsync(string orderId, int branchId)
    {
        var order = await GetDeliveryOrderOrThrowAsync(orderId, branchId);

        if (order.DeliveryStatus != DeliveryStatus.PendingAcceptance)
            throw new ValidationException("Order can only be accepted when status is PendingAcceptance.");

        order.DeliveryStatus = DeliveryStatus.Accepted;
        order.KitchenStatus = KitchenStatus.Pending;

        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Rejects a delivery order that is pending acceptance.
    /// </summary>
    public async Task<Order> RejectDeliveryOrderAsync(string orderId, string reason, int branchId)
    {
        var order = await GetDeliveryOrderOrThrowAsync(orderId, branchId);

        if (order.DeliveryStatus != DeliveryStatus.PendingAcceptance)
            throw new ValidationException("Order can only be rejected when status is PendingAcceptance.");

        order.DeliveryStatus = DeliveryStatus.Rejected;
        order.CancellationReason = reason;
        order.CancelledAt = DateTime.UtcNow;

        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Marks an accepted delivery order as ready for courier pickup.
    /// </summary>
    public async Task<Order> MarkReadyForPickupAsync(string orderId, int branchId)
    {
        var order = await GetDeliveryOrderOrThrowAsync(orderId, branchId);

        if (order.DeliveryStatus != DeliveryStatus.Accepted)
            throw new ValidationException("Order can only be marked ready when status is Accepted.");

        order.DeliveryStatus = DeliveryStatus.Ready;
        order.KitchenStatus = KitchenStatus.Ready;

        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Marks a ready delivery order as picked up by the courier.
    /// Sets IsPaid = true since delivery platforms pre-collect payment.
    /// </summary>
    public async Task<Order> MarkPickedUpAsync(string orderId, int branchId)
    {
        var order = await GetDeliveryOrderOrThrowAsync(orderId, branchId);

        if (order.DeliveryStatus != DeliveryStatus.Ready)
            throw new ValidationException("Order can only be marked picked up when status is Ready.");

        order.DeliveryStatus = DeliveryStatus.PickedUp;
        order.KitchenStatus = KitchenStatus.Delivered;
        order.IsPaid = true;

        _unitOfWork.Orders.Update(order);
        await _unitOfWork.SaveChangesAsync();
        return order;
    }

    /// <summary>
    /// Gets all active (non-terminal) delivery orders for a branch, mapped to DTOs.
    /// </summary>
    public async Task<IEnumerable<DeliveryOrderDto>> GetActiveDeliveryOrdersAsync(int branchId)
    {
        var orders = await _unitOfWork.Orders.GetActiveDeliveryOrdersAsync(branchId);

        return orders.Select(order => new DeliveryOrderDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            OrderSource = order.OrderSource.ToString(),
            ExternalOrderId = order.ExternalOrderId,
            DeliveryStatus = order.DeliveryStatus.ToString(),
            DeliveryCustomerName = order.DeliveryCustomerName,
            EstimatedPickupAt = order.EstimatedPickupAt,
            TotalCents = order.TotalCents,
            KitchenStatus = order.KitchenStatus.ToString(),
            CreatedAt = order.CreatedAt,
            Items = order.Items.Select(i => new DeliveryOrderItemDto
            {
                Id = i.Id,
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPriceCents = i.UnitPriceCents,
                Notes = i.Notes,
                SizeName = i.SizeName
            }).ToList()
        });
    }

    /// <summary>
    /// Ingests an order from an external delivery platform webhook.
    /// Rejects duplicates based on ExternalOrderId.
    /// </summary>
    public async Task<Order> IngestWebhookOrderAsync(IngestDeliveryOrderRequest request, int branchId, bool isPrepaidByPlatform)
    {
        var existing = await _unitOfWork.Orders.GetByExternalIdAsync(branchId, request.ExternalOrderId);
        if (existing != null)
            throw new ValidationException($"Duplicate delivery order: {request.ExternalOrderId}");

        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            OrderSource = request.Source,
            ExternalOrderId = request.ExternalOrderId,
            DeliveryStatus = DeliveryStatus.PendingAcceptance,
            DeliveryCustomerName = request.CustomerName,
            EstimatedPickupAt = request.EstimatedPickupAt,
            KitchenStatus = KitchenStatus.Pending,
            BranchId = branchId,
            TotalCents = request.TotalCents,
            SubtotalCents = request.TotalCents,
            IsPaid = isPrepaidByPlatform,
            SyncStatus = OrderSyncStatus.Synced,
            CreatedAt = DateTime.UtcNow,
            Items = request.Items.Select(i => new OrderItem
            {
                ProductName = i.ProductName,
                Quantity = i.Quantity,
                UnitPriceCents = i.UnitPriceCents,
                Notes = i.Notes
            }).ToList()
        };

        await _unitOfWork.Orders.AddAsync(order);
        await _unitOfWork.SaveChangesAsync();
        return order;
    }

    /// <inheritdoc />
    public async Task<Order> ValidateAndIngestWebhookAsync(
        OrderSource orderSource, int branchId, string? webhookSecret, IngestDeliveryOrderRequest request)
    {
        var config = await _unitOfWork.BranchDeliveryConfigs
            .GetByBranchAndPlatformAsync(branchId, orderSource);

        if (config == null)
            throw new NotFoundException($"Platform {orderSource} not configured for this branch.");

        if (!config.IsActive)
            throw new ValidationException($"Platform {orderSource} integration is not active.");

        if (string.IsNullOrEmpty(config.WebhookSecret) || webhookSecret != config.WebhookSecret)
            throw new UnauthorizedAccessException("Invalid webhook secret.");

        request.Source = orderSource;
        return await IngestWebhookOrderAsync(request, branchId, config.IsPrepaidByPlatform);
    }

    #endregion

    #region Private Helper Methods

    private async Task<Order> GetDeliveryOrderOrThrowAsync(string orderId, int branchId)
    {
        var results = await _unitOfWork.Orders.GetAsync(
            o => o.Id == orderId && o.BranchId == branchId);
        var order = results.FirstOrDefault();

        if (order == null)
            throw new NotFoundException($"Order {orderId} not found.");

        if (order.OrderSource == OrderSource.Direct)
            throw new ValidationException("This operation is only valid for delivery orders.");

        return order;
    }

    #endregion
}
