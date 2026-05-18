namespace POS.Domain.Enums;

/// <summary>
/// Universal product classification across verticals. Drives backend dispatch
/// (stock deduction path, sync engine hooks, fiscal/SAT defaults, UI rendering)
/// without relying on inferred metadata. Persisted as a string via
/// <c>HasConversion&lt;string&gt;()</c> so SQL stays self-describing.
/// </summary>
public enum ProductType
{
    /// <summary>F&amp;B prepared items without recipes; defaults; generic catalog item.</summary>
    Standard = 0,

    /// <summary>Discrete retail items with integer stock (Retail, Abarrotes, Refaccionaria).</summary>
    TrackedByUnit = 1,

    /// <summary>
    /// Items tracked by continuous measure (Weight, Volume, Length, Area) depending on SatUnitCode.
    /// Bulk/scale retail items with fractional stock — jamón al peso (KGM), aceite a granel
    /// (LTR), tela por metro (MTR), tile by m² (MTK), etc. Frontend resolves the physical unit
    /// from <c>OrderItem.SatUnitCode</c>; the enum only signals "sold by continuous unit".
    /// </summary>
    TrackedByMeasure = 2,

    /// <summary>Items composed of ingredients via <c>ProductConsumption</c> rows.</summary>
    Recipe = 3,

    /// <summary>Time-based bookable services (Estética, Consultorio, Taller, Gimnasio).</summary>
    Service = 4,

    /// <summary>Time-based entitlements (gym memberships, subscriptions).</summary>
    Membership = 5
}
