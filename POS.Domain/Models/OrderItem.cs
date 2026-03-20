using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public partial class OrderItem
{
    public int Id { get; set; }

    [Required]
    [MaxLength(36)]
    public string OrderId { get; set; } = null!;

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

    public virtual Order? Order { get; set; }

    public virtual Product? Product { get; set; }
}
