using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class OrderItemTax
{
    public int Id { get; set; }

    public int OrderItemId { get; set; }

    /// <summary>FK to Tax. Nullable to preserve snapshot even if Tax is deleted.</summary>
    public int? TaxId { get; set; }

    /// <summary>Tax name frozen at time of sale (e.g., "IVA 16%").</summary>
    [Required]
    [MaxLength(50)]
    public string TaxName { get; set; } = null!;

    /// <summary>Tax rate frozen at time of sale (e.g., 0.16).</summary>
    public decimal TaxRate { get; set; }

    /// <summary>Calculated tax amount in cents for this line item.</summary>
    public int TaxAmountCents { get; set; }

    public virtual OrderItem? OrderItem { get; set; }

    public virtual Tax? Tax { get; set; }
}
