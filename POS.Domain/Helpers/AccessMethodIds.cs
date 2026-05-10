namespace POS.Domain.Helpers;

/// <summary>
/// Stable numeric identifiers for <see cref="POS.Domain.Models.Catalogs.AccessMethodCatalog"/>
/// rows seeded by <c>DbInitializer.SeedSystemDataAsync</c>. Values mirror the
/// PostgreSQL identity sequence assigned to the seed AddRange order — never
/// renumber existing entries.
/// </summary>
public static class AccessMethodIds
{
    public const int Qr = 1;
    public const int Biometric = 2;
    public const int Manual = 3;
}
