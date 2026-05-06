namespace POS.Domain.Models.Metadata;

/// <summary>
/// Strongly-typed metadata payload stored on <see cref="Product"/>. Carries
/// vertical-specific product configuration as strict typed properties.
/// Persisted as PostgreSQL <c>jsonb</c> via EF Core 9 owned-type JSON mapping
/// (<c>OwnsOne(...).ToJson()</c>). Dynamic tenant-specific data lives on the
/// parent entity via <c>Product.ExtensionData</c>.
/// </summary>
public class ProductMetadata
{
    #region Services / Gym Vertical

    /// <summary>
    /// Days of membership granted when this product is sold (gym vertical).
    /// Zero or null means the product is not a membership.
    /// </summary>
    public int? MembershipDurationDays { get; set; }

    /// <summary>
    /// Length of the service slot in minutes when this product represents a
    /// bookable service (Estética, Consultorio, Taller, Gimnasio).
    /// Powers appointment-slot logic.
    /// </summary>
    public int? ServiceDurationMinutes { get; set; }

    #endregion

    #region Food &amp; Beverage / Quick Service Vertical

    /// <summary>
    /// Estimated kitchen preparation time in minutes. Drives KDS time
    /// estimates for restaurant and quick-service flows.
    /// </summary>
    public int? KitchenPrepMinutes { get; set; }

    /// <summary>
    /// Whether this product contains alcohol. Enables age-gating and
    /// regulatory compliance hooks for Bar, Cantina and Sports Bar sub-giros.
    /// </summary>
    public bool? IsAlcoholic { get; set; }

    #endregion

    #region Retail Vertical

    /// <summary>
    /// Whether this product is sold by weight (Enterprise plan scale support).
    /// Triggers the POS to capture the actual weight at sale time, populating
    /// <see cref="OrderItemMetadata.WeightGrams"/>.
    /// </summary>
    public bool? IsSoldByWeight { get; set; }

    #endregion
}
