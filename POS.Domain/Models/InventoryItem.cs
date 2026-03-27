using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class InventoryItem
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [Required]
    [MaxLength(10)]
    public string Unit { get; set; } = null!;

    public decimal CurrentStock { get; set; }

    public decimal LowStockThreshold { get; set; }

    public int CostCents { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Branch? Branch { get; set; }

    public virtual ICollection<InventoryMovement>? Movements { get; set; }

    public virtual ICollection<ProductConsumption>? ProductConsumptions { get; set; }
}
