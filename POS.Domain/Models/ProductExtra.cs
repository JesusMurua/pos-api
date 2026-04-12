using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public partial class ProductExtra
{
    public int Id { get; set; }

    /// <summary>
    /// FK to the modifier group this extra belongs to. Extras are always
    /// owned by a group, never by a product directly.
    /// </summary>
    public int ProductModifierGroupId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Label { get; set; } = null!;

    public int PriceCents { get; set; }

    /// <summary>Display order inside the owning modifier group.</summary>
    public int SortOrder { get; set; }

    public virtual ProductModifierGroup? ProductModifierGroup { get; set; }
}
