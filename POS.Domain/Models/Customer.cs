using System.ComponentModel.DataAnnotations;

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
    /// Membership validity expiration (UTC). Null when the customer has no active membership.
    /// Strict column for fast queries (e.g. "memberships expiring this week").
    /// Updated by <c>ExtendMembershipAsync</c> when a membership product is sold.
    /// </summary>
    public DateTime? MembershipValidUntil { get; set; }

    /// <summary>
    /// Timestamp of the last membership/recurring payment (UTC).
    /// Useful for churn analytics and "last seen paying" reports.
    /// </summary>
    public DateTime? LastPaymentAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual Business? Business { get; set; }

    public virtual ICollection<Order>? Orders { get; set; }

    public virtual ICollection<Reservation>? Reservations { get; set; }

    public virtual ICollection<CustomerTransaction>? Transactions { get; set; }
}
