using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

/// <summary>
/// Entitlement aggregate representing a customer's membership purchase. A single
/// <see cref="Customer"/> may hold multiple memberships concurrently (one per
/// <see cref="ProductId"/>). Drives the gym/fitness vertical and powers the
/// <c>ProcessOrderEntitlementsAsync</c> sync hook from <c>OrderService</c>.
/// </summary>
public class CustomerMembership
{
    public int Id { get; set; }

    /// <summary>FK to the customer who owns this entitlement. Cascade-deleted with the customer.</summary>
    public int CustomerId { get; set; }

    /// <summary>
    /// FK to the membership product. Nullable to accommodate legacy or admin-issued
    /// memberships that pre-date a concrete product reference; new sales always set this.
    /// </summary>
    public int? ProductId { get; set; }

    /// <summary>UTC start of the membership period.</summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>UTC expiration of the membership period.</summary>
    public DateTime ValidUntil { get; set; }

    /// <summary>
    /// Lifecycle status. Defaults to <see cref="MembershipStatus.Active"/>. Status
    /// transitions to <see cref="MembershipStatus.Expired"/> are projected lazily by
    /// query callers when <c>ValidUntil &lt; UtcNow</c>; only <see cref="MembershipStatus.Frozen"/>
    /// and <see cref="MembershipStatus.Cancelled"/> are written explicitly.
    /// </summary>
    public MembershipStatus Status { get; set; } = MembershipStatus.Active;

    /// <summary>FK (UUID) to the order whose sync produced this membership. Null for admin-issued grants.</summary>
    [MaxLength(36)]
    public string? OriginatingOrderId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual Product? Product { get; set; }

    public virtual Order? OriginatingOrder { get; set; }
}
