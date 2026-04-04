namespace POS.Domain.Enums;

/// <summary>
/// Typed unit of measure for inventory ingredients.
/// Supersedes the legacy free-text <c>Unit</c> string on <c>InventoryItem</c>.
/// </summary>
public enum UnitOfMeasure
{
    /// <summary>Kilograms — solid bulk weight (e.g., 1 kg of chicken).</summary>
    Kg = 0,

    /// <summary>Grams — small solid weight (e.g., 200 g of cheese).</summary>
    G = 1,

    /// <summary>Liters — liquid volume (e.g., 1 L of oil).</summary>
    L = 2,

    /// <summary>Milliliters — small liquid volume (e.g., 250 mL of sauce).</summary>
    mL = 3,

    /// <summary>Pieces / units — discrete countable items (e.g., 12 eggs).</summary>
    Pcs = 4,

    /// <summary>Ounces — weight used for some imported products.</summary>
    Oz = 5
}
