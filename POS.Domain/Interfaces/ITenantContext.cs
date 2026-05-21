namespace POS.Domain.Interfaces;

/// <summary>
/// Per-request tenant identity sourced from the caller's JWT claims
/// (<c>branchId</c> and <c>businessId</c>). Consumed by the DbContext to
/// apply tenant-scoped global query filters.
/// Both values are nullable: when no HttpContext is bound (background
/// services, EF design-time tooling, migrations, seeding) or the claims
/// are absent the filters degrade to a no-op, letting internal jobs see
/// every tenant.
/// </summary>
public interface ITenantContext
{
    /// <summary>Current branch identifier from the JWT, or <c>null</c> when unscoped.</summary>
    int? BranchId { get; }

    /// <summary>Current business identifier from the JWT, or <c>null</c> when unscoped.</summary>
    int? BusinessId { get; }
}
