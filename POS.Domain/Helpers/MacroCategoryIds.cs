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
}
