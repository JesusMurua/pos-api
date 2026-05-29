using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace POS.API.Auth;

/// <summary>
/// Authenticates ops-only admin endpoints (e.g. catalog cache invalidation)
/// by validating an opaque <c>X-Admin-Token</c> header against the
/// <c>ADMIN_API_TOKEN</c> environment variable. Coexists with the default
/// JWT Bearer scheme: end-user controllers stay on JWT, admin controllers
/// opt in via <c>[Authorize(AuthenticationSchemes = "AdminToken")]</c>.
/// <para>
/// The token comparison uses <see cref="CryptographicOperations.FixedTimeEquals"/>
/// to defeat timing side-channels. On success the produced
/// <see cref="ClaimsPrincipal"/> carries a hashed token id (first 8 hex chars
/// of SHA-256) under the <c>token_id</c> claim so audit logs can attribute
/// the action without exposing the secret itself.
/// </para>
/// </summary>
public class AdminTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "AdminToken";
    public const string TokenIdClaimType = "token_id";

    private const string HeaderName = "X-Admin-Token";

    private readonly string? _expectedToken;

    public AdminTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : base(options, logger, encoder)
    {
        _expectedToken = configuration["Admin:ApiToken"];
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var headerValues))
        {
            // NoResult — not Fail — so other schemes still get a chance and
            // the caller receives a clean 401 from the [Authorize] filter
            // rather than a "missing header" error body.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var provided = headerValues.ToString();
        if (string.IsNullOrEmpty(provided))
        {
            return Task.FromResult(AuthenticateResult.Fail("Empty X-Admin-Token header"));
        }

        if (string.IsNullOrEmpty(_expectedToken))
        {
            // In dev/test the token may legitimately be unset; the request
            // simply fails to authenticate under this scheme and the
            // [Authorize] filter responds 401.
            return Task.FromResult(AuthenticateResult.Fail("Admin token not configured"));
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var expectedBytes = Encoding.UTF8.GetBytes(_expectedToken);

        if (providedBytes.Length != expectedBytes.Length ||
            !CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid X-Admin-Token value"));
        }

        var tokenId = ComputeTokenId(provided);
        var claims = new[]
        {
            new Claim(ClaimTypes.Name, $"admin-token-{tokenId}"),
            new Claim(TokenIdClaimType, tokenId)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    /// <summary>
    /// Derives a short, irreversible identifier from the admin token (first
    /// 8 hex chars of SHA-256) so log lines can attribute the caller without
    /// ever serializing the secret itself.
    /// </summary>
    public static string ComputeTokenId(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash)[..8].ToLowerInvariant();
    }
}
