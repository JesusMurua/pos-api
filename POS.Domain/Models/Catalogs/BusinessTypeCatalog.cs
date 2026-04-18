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

    public MacroCategory? PrimaryMacroCategory { get; set; }
}
