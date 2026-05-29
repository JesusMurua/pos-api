namespace POS.Domain.Helpers;

/// <summary>
/// Canonical cluster slugs for sub-giros under Macro 4 (Services). Mirrors the
/// 10 clusters declared in <c>docs/SUB-GIROS-TAXONOMY.md</c>; the database
/// enforces the same whitelist via a CHECK constraint on
/// <c>BusinessTypeCatalog.ClusterCode</c>. Defense in depth: app-level
/// constants stop typos at compile time, DB constraint stops bad rows
/// inserted out-of-band (SQL console, future ETL).
/// </summary>
public static class ClusterCodes
{
    public const string Beauty = "beauty";
    public const string Health = "health";
    public const string Automotive = "automotive";
    public const string Pets = "pets";
    public const string Repair = "repair";
    public const string Fitness = "fitness";
    public const string Education = "education";
    public const string Home = "home";
    public const string Events = "events";
    public const string Professional = "professional";

    /// <summary>
    /// Immutable lookup of every valid cluster slug. Used by validation and
    /// keeps the CHECK constraint string in
    /// <see cref="POS.Repository.ApplicationDbContext"/> in lockstep with the
    /// constants above — a missed slug fails both layers consistently.
    /// </summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        Beauty,
        Health,
        Automotive,
        Pets,
        Repair,
        Fitness,
        Education,
        Home,
        Events,
        Professional
    };
}
