using POS.Domain.Models;

namespace POS.Services.IService;

public interface IDeliveryService
{
    Task<Order> AcceptDeliveryOrderAsync(string orderId, int branchId);
    Task<Order> RejectDeliveryOrderAsync(string orderId, string reason, int branchId);
    Task<Order> MarkReadyForPickupAsync(string orderId, int branchId);
    Task<Order> MarkPickedUpAsync(string orderId, int branchId);
    Task<IEnumerable<Order>> GetActiveDeliveryOrdersAsync(int branchId);
    Task<Order> IngestWebhookOrderAsync(IngestDeliveryOrderRequest request, int branchId);
}
