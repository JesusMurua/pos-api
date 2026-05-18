namespace POS.Domain.Models.Metadata;

/// <summary>
/// Strongly-typed metadata payload stored at the <see cref="OrderItem"/> line
/// level. Used for per-line vertical data that does not belong on the global
/// order. Persisted as PostgreSQL <c>jsonb</c> via EF Core 9 owned-type JSON
/// mapping. Dynamic tenant-specific data lives on the parent entity via
/// <c>OrderItem.ExtensionData</c>.
/// </summary>
public class OrderItemMetadata
{
    #region Services / Gym Vertical

    /// <summary>
    /// Customer who receives the entitlement when this line is purchased on
    /// behalf of someone other than the order's payor (<see cref="Order.CustomerId"/>).
    /// Null means the payor is also the beneficiary.
    /// </summary>
    public int? BeneficiaryCustomerId { get; set; }

    /// <summary>
    /// Scheduled appointment time for service-based sub-giros (Estética,
    /// Consultorio, Taller, Gimnasio). Distinct from <see cref="Order.CreatedAt"/>;
    /// represents when the service will actually be delivered.
    /// </summary>
    public DateTime? AppointmentAt { get; set; }

    #endregion
}
