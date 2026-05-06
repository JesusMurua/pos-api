namespace POS.Domain.DTOs.Customer;

/// <summary>
/// Lightweight projection of a <see cref="POS.Domain.Models.CustomerMembership"/>
/// row used by the Admin "Customer Detail → Membership" panel. The
/// <see cref="Status"/> string is computed at query time so that lazy-expired
/// rows (DB stores <c>Active</c> but <c>ValidUntil &lt; UtcNow</c>) surface as
/// <c>"Expired"</c> per BDD-019 §6.1.2.
/// </summary>
public class CustomerMembershipDto
{
    public int Id { get; set; }

    /// <summary>Owner of the membership. Populated by every projection so the
    /// dashboard "Expiring Soon" widget can render rows without an extra
    /// round-trip per customer.</summary>
    public int CustomerId { get; set; }

    /// <summary>Display name for the customer. Concatenated as
    /// <c>FirstName + " " + LastName</c> with null-safe handling when the
    /// customer has no last name. Empty string is never returned for a valid
    /// customer.</summary>
    public string CustomerName { get; set; } = string.Empty;

    public int? ProductId { get; set; }

    public string? ProductName { get; set; }

    public DateTime ValidFrom { get; set; }

    public DateTime ValidUntil { get; set; }

    /// <summary>Status as a wire-friendly string. Lazy-Expired rows project as <c>"Expired"</c>.</summary>
    public string Status { get; set; } = null!;

    public string? OriginatingOrderId { get; set; }

    public DateTime CreatedAt { get; set; }
}
