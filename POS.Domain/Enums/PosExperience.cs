namespace POS.Domain.Enums;

/// <summary>
/// POS UI experience variant. Numeric values are explicit so that an
/// uninitialized <c>int</c> field surfaces as <see cref="Unknown"/> (0),
/// not as <see cref="Restaurant"/> — preventing the silent default-fallback
/// that previously routed every unmapped business to the restaurant UI.
/// Values 1-N are intentionally aligned with <c>MacroCategory.PosExperience</c>
/// string codes ("Restaurant", "Counter", "Retail", "Services", "Quick").
/// </summary>
public enum PosExperience
{
    Unknown = 0,
    Restaurant = 1,
    Counter = 2,
    Retail = 3,
    Services = 4,
    Quick = 5
}
