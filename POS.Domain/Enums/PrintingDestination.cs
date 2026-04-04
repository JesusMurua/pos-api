namespace POS.Domain.Enums;

/// <summary>
/// Defines the physical area where an order item ticket must be printed.
/// Stored as an integer in the database (explicit values guarantee stability across environments).
/// Default value is <see cref="Kitchen"/> for backwards compatibility with existing products.
/// </summary>
public enum PrintingDestination
{
    /// <summary>
    /// Hot or cold kitchen — food preparation items.
    /// Legacy default; all pre-existing products map to this value.
    /// </summary>
    Kitchen = 0,

    /// <summary>
    /// Bar area — beverages, coffees, juices.
    /// </summary>
    Bar = 1,

    /// <summary>
    /// Floor / waiters station — items that require no preparation
    /// (e.g., bottled water, pre-packaged condiments).
    /// </summary>
    Waiters = 2
}
