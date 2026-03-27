using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class InventoryMovement
{
    public int Id { get; set; }

    public int? InventoryItemId { get; set; }

    public int? ProductId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Type { get; set; } = null!;

    public decimal Quantity { get; set; }

    [MaxLength(500)]
    public string? Reason { get; set; }

    [MaxLength(36)]
    public string? OrderId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public int? CreatedByUserId { get; set; }

    public virtual InventoryItem? InventoryItem { get; set; }
}
