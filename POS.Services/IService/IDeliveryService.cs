using POS.Domain.Enums;
using POS.Domain.Models;

namespace POS.Services.IService;

public interface IDeliveryService
{
    Task<Order> AcceptDeliveryOrderAsync(string orderId, int branchId);
    Task<Order> RejectDeliveryOrderAsync(string orderId, string reason, int branchId);
    Task<Order> MarkReadyForPickupAsync(string orderId, int branchId);
    Task<Order> MarkPickedUpAsync(string orderId, int branchId);
    Task<IEnumerable<DeliveryOrderDto>> GetActiveDeliveryOrdersAsync(int branchId);
    Task<Order> IngestWebhookOrderAsync(IngestDeliveryOrderRequest request, int branchId, bool isPrepaidByPlatform);

    /// <summary>
    /// Validates the delivery platform config and webhook secret, then ingests the order.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Platform not configured.</exception>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">Platform inactive or invalid secret.</exception>
    /// <exception cref="UnauthorizedAccessException">Invalid webhook secret.</exception>
    Task<Order> ValidateAndIngestWebhookAsync(
        OrderSource orderSource, int branchId, string? webhookSecret, IngestDeliveryOrderRequest request);
}
