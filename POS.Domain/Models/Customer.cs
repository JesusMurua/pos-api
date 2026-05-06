using System.ComponentModel.DataAnnotations;
using POS.Domain.Models.Metadata;

namespace POS.Domain.Models;

/// <summary>
/// Represents a known customer of the business for CRM, store credit (fiado), and loyalty.
/// Scoped to Business — a customer is shared across all branches.
/// </summary>
public class Customer
{
    public int Id { get; set; }

    /// <summary>Business that owns this customer record.</summary>
    public int BusinessId { get; set; }

    /// <summary>Customer's first name.</summary>
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = null!;

    /// <summary>Customer's last name.</summary>
    [MaxLength(100)]
    public string? LastName { get; set; }

    /// <summary>Phone number. Unique per business when not null.</summary>
    [MaxLength(20)]
    public string? Phone { get; set; }

    /// <summary>Email address.</summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>
    /// Current loyalty points balance.
    /// Denormalized from CustomerTransaction ledger for performance.
    /// </summary>
    public int PointsBalance { get; set; } = 0;

    /// <summary>
    /// Current credit balance in cents (fiado).
    /// Positive = customer has prepaid credit (saldo a favor).
    /// Negative = customer owes the business (fiado / tab).
    /// Denormalized from CustomerTransaction ledger for performance.
    /// </summary>
    public int CreditBalanceCents { get; set; } = 0;

    /// <summary>
    /// Maximum credit (fiado) limit in cents. 0 = no limit (full trust).
    /// Enforced as: abs(CreditBalanceCents) must not exceed CreditLimitCents.
    /// </summary>
    public int CreditLimitCents { get; set; } = 0;

    /// <summary>Internal notes about this customer.</summary>
    [MaxLength(500)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Vertical-specific extensibility payload at the customer aggregate level,
    /// persisted as PostgreSQL <c>jsonb</c> via EF Core 9 owned-type JSON
    /// mapping. Carries CRM attributes (date of birth, marketing opt-in,
    /// emergency contact for fitness verticals).
    /// </summary>
    public CustomerMetadata? Metadata { get; set; }

    /// <summary>
    /// Dynamic tenant-specific data. CRITICAL: Lifecycle is managed by EF.
    /// Access RootElement for reads, but CLONE/COPY values if the entity will
    /// be detached/disposed to avoid ObjectDisposedException.
    /// </summary>
    public System.Text.Json.JsonDocument? ExtensionData { get; set; }

    /// <summary>
    /// Timestamp of the last recurring/membership payment (UTC). Universal CRM
    /// metric retained on <see cref="Customer"/> for churn analytics; the
    /// per-period membership history lives on <see cref="CustomerMembership"/>.
    /// </summary>
    public DateTime? LastPaymentAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual Business? Business { get; set; }

    public virtual ICollection<Order>? Orders { get; set; }

    public virtual ICollection<Reservation>? Reservations { get; set; }

    public virtual ICollection<CustomerTransaction>? Transactions { get; set; }

    /// <summary>
    /// Active and historical membership entitlements held by this customer.
    /// A customer may hold multiple concurrent memberships for different products.
    /// </summary>
    public virtual ICollection<CustomerMembership> Memberships { get; set; } = new List<CustomerMembership>();
}
