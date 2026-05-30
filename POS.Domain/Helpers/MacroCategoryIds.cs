namespace POS.Domain.Helpers;

/// <summary>
/// Stable integer identifiers for the rows seeded in <c>MacroCategory</c>.
/// All behavioral attributes (POS experience, kitchen/table flags, public names)
/// live as columns on the <c>MacroCategory</c> entity — this helper only exposes
/// the ids so that FK-referencing code can compile without string literals.
/// </summary>
public static class MacroCategoryIds
{
    public const int FoodBeverage = 1;
    public const int QuickService = 2;
    public const int Retail = 3;
    public const int Services = 4;

    /// <summary>
    /// Resolves a macro category id to its public <c>InternalCode</c>
    /// (<c>food-beverage</c>, <c>quick-service</c>, <c>retail</c>,
    /// <c>services</c>) without a database hit. The hardcoded mapping
    /// mirrors the seeded <c>MacroCategory.InternalCode</c> rows; keep the
    /// two in sync. Mirrors the shape of <see cref="PlanTypeIds.ToCode"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="id"/> is not one of the four seeded macro
    /// category ids — surface the unknown id instead of silently masking it
    /// behind a default.
    /// </exception>
    public static string ToCode(int id) => id switch
    {
        FoodBeverage => "food-beverage",
        QuickService => "quick-service",
        Retail => "retail",
        Services => "services",
        _ => throw new ArgumentOutOfRangeException(nameof(id), id,
            "Unknown MacroCategoryId; expected 1, 2, 3, or 4.")
    };
}
