namespace POS.IntegrationTests.Infrastructure;

/// <summary>
/// Deterministic secrets and identifiers shared by the JWT factory, the web
/// host configuration overrides, and the test fixtures. Keeping them in one
/// place guarantees the same key signs the token and validates it.
/// </summary>
internal static class TestConstants
{
    /// <summary>
    /// HMAC-SHA256 symmetric key used to sign and validate test JWTs.
    /// Must be at least 32 bytes for HS256; 64 bytes leaves headroom.
    /// </summary>
    public const string JwtSecret =
        "integration-test-jwt-secret-key-with-sufficient-length-for-hs256-validation";

    /// <summary>Issuer claim — matches the value in appsettings.json.</summary>
    public const string JwtIssuer = "POS.API";

    /// <summary>Audience claim — matches the value in appsettings.json.</summary>
    public const string JwtAudience = "POS.Frontend";

    /// <summary>
    /// HMAC secret required by <c>IHmacService</c> at host boot. Must be
    /// at least 32 bytes or the cryptography singleton fail-fasts.
    /// </summary>
    public const string AccessControlHmacSecret =
        "integration-test-access-control-hmac-secret-32-bytes-or-more!";

    /// <summary>
    /// Opaque admin token consumed by the <c>X-Admin-Token</c> header on
    /// ops-only endpoints (e.g. catalog cache invalidation). Production
    /// requires ≥ 32 chars; the dummy here matches that floor so the same
    /// length validation runs in tests as in prod.
    /// </summary>
    public const string AdminApiToken =
        "test-admin-token-dummy-for-integration-tests-32plus";

    /// <summary>Test tenant A — seeded by ApplicationDbContext.HasData.</summary>
    public const int BusinessAId = 1;
    public const int BranchAId = 1;
    public const int UserAId = 1;

    /// <summary>Test tenant B — created on demand by the test fixture.</summary>
    public const int BusinessBId = 9001;
    public const int BranchBId = 9001;
}
