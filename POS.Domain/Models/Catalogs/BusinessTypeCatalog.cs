using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Public-facing sub-giro identity (e.g., "Restaurante", "Cafetería", "Abarrotes").
/// Each sub-giro is anchored to a <see cref="MacroCategory"/> that dictates business rules.
/// A business may select multiple sub-giros via the <c>BusinessGiro</c> junction.
/// </summary>
public class BusinessTypeCatalog
{
    public int Id { get; set; }

    /// <summary>FK to <see cref="MacroCategory"/>. Drives plan/feature rules.</summary>
    public int PrimaryMacroCategoryId { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    /// <summary>
    /// Optional grouping slug for sub-giros that share a UX cluster (e.g. <c>beauty</c>,
    /// <c>automotive</c>). Populated only for Macro 4 (Services) entries; NULL for
    /// every other macro. Constrained at the DB level to the 10 canonical slugs
    /// declared in <see cref="Helpers.ClusterCodes"/>.
    /// </summary>
    [MaxLength(50)]
    public string? ClusterCode { get; set; }

    public MacroCategory? PrimaryMacroCategory { get; set; }
}
