using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

/// <summary>
/// A named, rule-bound group of <see cref="ProductExtra"/> options attached
/// to a single <see cref="Product"/>. Introduces semantic grouping
/// ("Choose your protein", "Sauces") and selection constraints on top of
/// what was previously a flat list of extras.
/// </summary>
public partial class ProductModifierGroup
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>Display order inside the product's modifier list.</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Semantic shortcut for <c>MinSelectable &gt;= 1</c>. Kept as a
    /// discrete column so UI consumers don't need to derive it.
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>Minimum number of extras the user must pick. Zero = optional.</summary>
    public int MinSelectable { get; set; }

    /// <summary>
    /// Maximum number of extras the user may pick. One = single-choice
    /// (radio); values greater than one = multi-choice (checkbox).
    /// </summary>
    public int MaxSelectable { get; set; }

    public virtual Product? Product { get; set; }

    public virtual ICollection<ProductExtra> Extras { get; set; } = new List<ProductExtra>();
}
