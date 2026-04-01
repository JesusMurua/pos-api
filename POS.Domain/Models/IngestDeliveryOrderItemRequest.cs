using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class IngestDeliveryOrderItemRequest
{
    [Required]
    [MaxLength(150)]
    public string ProductName { get; set; } = null!;

    [Required]
    public int Quantity { get; set; }

    [Required]
    public int UnitPriceCents { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}
