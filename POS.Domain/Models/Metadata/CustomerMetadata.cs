namespace POS.Domain.Models.Metadata;

/// <summary>
/// Strongly-typed metadata payload stored on <see cref="Customer"/>. Captures
/// universal CRM attributes plus vertical-specific safety/contact fields.
/// Persisted as PostgreSQL <c>jsonb</c> via EF Core 9 owned-type JSON mapping.
/// Dynamic tenant-specific data lives on the parent entity via
/// <c>Customer.ExtensionData</c>.
/// </summary>
public class CustomerMetadata
{
    #region Universal CRM

    /// <summary>
    /// Customer's date of birth. Powers birthday marketing campaigns and
    /// age-gated flows (e.g. gym membership minimum age).
    /// </summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>
    /// Whether the customer has opted in to marketing communications
    /// (SMS / Email). Required for GDPR / LFPDPPP-aligned campaigns.
    /// </summary>
    public bool? MarketingOptIn { get; set; }

    #endregion

    #region Services / Gym &amp; Wellness Vertical

    /// <summary>
    /// Emergency contact phone number. Standard fitness/wellness vertical
    /// field driven by safety regulations for venues handling physical
    /// activity members.
    /// </summary>
    public string? EmergencyContactPhone { get; set; }

    #endregion
}
