using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Coarse-grained classification that drives business rules: POS experience,
/// plan eligibility, feature gating, Stripe pricing group. Independent of
/// the business's public-facing giro list (see <see cref="BusinessTypeCatalog"/>).
/// </summary>
public class MacroCategory
{
    public int Id { get; set; }

    [Required, MaxLength(30)]
    public string InternalCode { get; set; } = null!;

    [Required, MaxLength(100)]
    public string PublicName { get; set; } = null!;

    [MaxLength(300)]
    public string? Description { get; set; }

    /// <summary>
    /// Frontend POS experience variant rendered for businesses under this macro
    /// ("Restaurant", "Counter", "Retail", "Services").
    /// </summary>
    [Required, MaxLength(30)]
    public string PosExperience { get; set; } = string.Empty;

    /// <summary>Whether this macro drives a kitchen-centric workflow (KDS, commandas).</summary>
    public bool HasKitchen { get; set; }

    /// <summary>Whether this macro drives a table/dine-in workflow (table map, waiter app).</summary>
    public bool HasTables { get; set; }
}
