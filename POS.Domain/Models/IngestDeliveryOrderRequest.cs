using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

public class IngestDeliveryOrderRequest
{
    [Required]
    public OrderSource Source { get; set; }

    [Required]
    [MaxLength(50)]
    public string ExternalOrderId { get; set; } = null!;

    [MaxLength(100)]
    public string? CustomerName { get; set; }

    [Required]
    public int TotalCents { get; set; }

    public DateTime? EstimatedPickupAt { get; set; }

    [Required]
    public List<IngestDeliveryOrderItemRequest> Items { get; set; } = new();
}
