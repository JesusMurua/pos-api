using POS.Domain.Interfaces;
using POS.Domain.Models.Catalogs;

namespace POS.Domain.Models;

/// <summary>
/// Append-only audit row written every time the gym/access-control hardware
/// (turnstile, biometric reader, reception terminal) evaluates a customer's
/// entitlement. Authoritative source for "who entered, when, why, and how".
/// Implements <see cref="IBranchScoped"/> so <c>BranchInjectionInterceptor</c>
/// overwrites <see cref="BranchId"/> from the caller's JWT and the bridge
/// cannot spoof a row into a sibling branch.
/// </summary>
public class AccessLog : IBranchScoped
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    public int CustomerId { get; set; }

    /// <summary>FK to the device that evaluated the access. Null for manual admin overrides.</summary>
    public int? DeviceId { get; set; }

    /// <summary>FK to the membership that authorized (or failed to authorize) the access.</summary>
    public int? CustomerMembershipId { get; set; }

    public int AccessReasonId { get; set; }

    public int AccessMethodId { get; set; }

    public bool IsGranted { get; set; }

    /// <summary>UTC timestamp when the physical access event occurred.</summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>UTC timestamp when the row was persisted (may differ from <see cref="OccurredAt"/> on backfill).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Branch? Branch { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual Device? Device { get; set; }

    public virtual CustomerMembership? CustomerMembership { get; set; }

    public virtual AccessReasonCatalog? AccessReason { get; set; }

    public virtual AccessMethodCatalog? AccessMethod { get; set; }
}
