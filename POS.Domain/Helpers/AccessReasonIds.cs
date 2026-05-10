namespace POS.Domain.Helpers;

/// <summary>
/// Stable numeric identifiers for <see cref="POS.Domain.Models.Catalogs.AccessReasonCatalog"/>
/// rows seeded by <c>DbInitializer.SeedSystemDataAsync</c>. Values mirror the
/// PostgreSQL identity sequence assigned to the seed AddRange order — never
/// renumber existing entries.
/// </summary>
public static class AccessReasonIds
{
    public const int MembershipActive = 1;
    public const int PaymentOverdue = 2;
    public const int MembershipExpired = 3;
    public const int NoMembership = 4;
    public const int ManualOverride = 5;
}
