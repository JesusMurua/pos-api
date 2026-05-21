using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace POS.IntegrationTests.Infrastructure;

/// <summary>
/// Forges JWT bearer tokens that the test web host accepts as valid.
/// Tokens are signed with the same symmetric key and carry the same
/// issuer/audience values configured on <see cref="CustomWebApplicationFactory"/>,
/// and replicate the claim set emitted by <c>AuthService</c> in production
/// so downstream consumers (<c>BaseApiController</c>, <c>HttpTenantContext</c>,
/// authorization filters) behave exactly as they would for a real login.
/// </summary>
internal static class JwtTestFactory
{
    /// <summary>
    /// Builds a user-issued token, mirroring <c>AuthService.GenerateToken</c>.
    /// </summary>
    public static string CreateUserToken(int businessId, int branchId, int userId, string role)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Role, role),
            new(ClaimTypes.Name, $"TestUser-{userId}"),
            new("businessId", businessId.ToString()),
            new("branchId", branchId.ToString()),
            new("branches", $"[{{\"id\":{branchId},\"name\":\"Test Branch\"}}]"),
            new("planType", "basic"),
            new("macroCategory", "food-beverage"),
            new("trialEndsAt", string.Empty),
            new("onboardingCompleted", "true"),
            new("features", "[]"),
            new("sessionType", "email")
        };

        return WriteToken(claims, TimeSpan.FromHours(1));
    }

    /// <summary>
    /// Builds a device-issued token, mirroring <c>AuthService.GenerateDeviceToken</c>.
    /// </summary>
    public static string CreateDeviceToken(int businessId, int branchId, string mode, string[] features)
    {
        var featuresJson = features.Length == 0
            ? "[]"
            : "[\"" + string.Join("\",\"", features) + "\"]";

        var claims = new List<Claim>
        {
            new("type", "device"),
            new("deviceId", "1"),
            new("businessId", businessId.ToString()),
            new("branchId", branchId.ToString()),
            new("mode", mode),
            new("planType", "basic"),
            new("macroCategory", "food-beverage"),
            new("features", featuresJson)
        };

        return WriteToken(claims, TimeSpan.FromHours(1));
    }

    private static string WriteToken(IEnumerable<Claim> claims, TimeSpan lifetime)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestConstants.JwtSecret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: TestConstants.JwtIssuer,
            audience: TestConstants.JwtAudience,
            claims: claims,
            expires: DateTime.UtcNow.Add(lifetime),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
