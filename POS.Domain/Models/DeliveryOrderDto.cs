namespace POS.Domain.Models;

/// <summary>
/// DTO for active delivery orders returned by GET /api/delivery/active.
/// </summary>
public class DeliveryOrderDto
{
    public string Id { get; set; } = null!;
    public int OrderNumber { get; set; }
    public string OrderSource { get; set; } = null!;
    public string? ExternalOrderId { get; set; }
    public string DeliveryStatus { get; set; } = null!;
    public string? DeliveryCustomerName { get; set; }
    public DateTime? EstimatedPickupAt { get; set; }
    public int TotalCents { get; set; }
    public string KitchenStatus { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public List<DeliveryOrderItemDto> Items { get; set; } = new();
}

/// <summary>
/// DTO for individual items within a delivery order.
/// </summary>
public class DeliveryOrderItemDto
{
    public int Id { get; set; }
    public string ProductName { get; set; } = null!;
    public int Quantity { get; set; }
    public int UnitPriceCents { get; set; }
    public string? Notes { get; set; }
    public string? SizeName { get; set; }
}
