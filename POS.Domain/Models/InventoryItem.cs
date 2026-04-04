using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

public class InventoryItem
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Legacy free-text unit string. Kept for backwards compatibility.
    /// New code should read and write <see cref="UnitOfMeasure"/> instead.
    /// </summary>
    [MaxLength(10)]
    public string Unit { get; set; } = null!;

    /// <summary>
    /// Typed unit of measure. Supersedes <see cref="Unit"/>.
    /// Default is <see cref="UnitOfMeasure.Pcs"/>.
    /// </summary>
    public UnitOfMeasure UnitOfMeasure { get; set; } = UnitOfMeasure.Pcs;

    public decimal CurrentStock { get; set; }

    public decimal LowStockThreshold { get; set; }

    /// <summary>Cost per unit in cents at the time of last purchase.</summary>
    public int CostCents { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual Branch? Branch { get; set; }

    public virtual ICollection<InventoryMovement>? Movements { get; set; }

    public virtual ICollection<ProductConsumption>? ProductConsumptions { get; set; }
}
