namespace POS.Domain.Models.Metadata;

/// <summary>
/// Strongly-typed metadata payload stored on <see cref="Order"/> at the header
/// level. Captures vertical-specific order attributes that are universal enough
/// to deserve strict typing. Persisted as PostgreSQL <c>jsonb</c> via EF Core 9
/// owned-type JSON mapping. Dynamic tenant-specific data lives on the parent
/// entity via <c>Order.ExtensionData</c>.
/// </summary>
public class OrderMetadata
{
    #region Food &amp; Beverage / Restaurant Vertical

    /// <summary>
    /// Party size for this order (number of diners). Standard restaurant POS
    /// metric used for table-sizing analytics and dish-count heuristics.
    /// </summary>
    public int? DiningPersons { get; set; }

    #endregion

    #region Delivery Platforms

    /// <summary>
    /// Full delivery address line for orders arriving from delivery platforms
    /// (UberEats, Rappi, DidiFood). Complements the strict
    /// <see cref="Order.DeliveryCustomerName"/> column so the printed commanda
    /// can render the full destination.
    /// </summary>
    public string? DeliveryAddressLine { get; set; }

    #endregion
}
