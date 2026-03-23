using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class SyncOrderRequest
{
    [Required]
    [MaxLength(36)]
    public string Id { get; set; } = null!;

    public int BranchId { get; set; }

    public int OrderNumber { get; set; }

    public int TotalCents { get; set; }

    [Required]
    public string PaymentMethod { get; set; } = null!;

    public int? TenderedCents { get; set; }

    public int? ChangeCents { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? TableId { get; set; }

    [MaxLength(50)]
    public string? TableName { get; set; }

    public List<SyncOrderItemRequest> Items { get; set; } = new();
}

public class SyncOrderItemRequest
{
    public int ProductId { get; set; }

    [Required]
    [MaxLength(150)]
    public string ProductName { get; set; } = null!;

    public int Quantity { get; set; }

    public int UnitPriceCents { get; set; }

    [MaxLength(50)]
    public string? SizeName { get; set; }

    public string? ExtrasJson { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}
