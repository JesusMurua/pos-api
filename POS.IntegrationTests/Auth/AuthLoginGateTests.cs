using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Auth;

/// <summary>
/// Verifies that the Business.IsActive suspension flag is enforced at the
/// login boundary (BDD security fix). Previously a suspended tenant could
/// still authenticate via /api/Auth/email-login or /api/Auth/pin-login —
/// only /api/Auth/me blocked the session post-hoc. The fix loads the
/// business immediately after credential validation and short-circuits
/// with a generic error message so attackers cannot distinguish a
/// suspended tenant from an unknown account.
/// </summary>
public class AuthLoginGateTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string EmailLoginRoute = "/api/Auth/email-login";
    private const string PinLoginRoute = "/api/Auth/pin-login";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthLoginGateTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region Email login — suspended business is rejected

    [Fact]
    public async Task EmailLogin_With_SuspendedBusiness_Returns_Generic_400()
    {
        var (email, password) = await SeedFreshOwnerAsync(businessActive: false);

        var response = await _client.PostAsJsonAsync(EmailLoginRoute, new
        {
            email,
            password
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a suspended tenant must be rejected at the email-login boundary");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("suspend", "the client message must not leak the suspension reason — info-leak prevention");
        body.Should().NotContain("inactive", "same info-leak rule");
    }

    #endregion

    #region Email login — active business with valid credentials still succeeds (control)

    [Fact]
    public async Task EmailLogin_With_ActiveBusiness_Returns_200()
    {
        var (email, password) = await SeedFreshOwnerAsync(businessActive: true);

        var response = await _client.PostAsJsonAsync(EmailLoginRoute, new
        {
            email,
            password
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an active tenant with valid credentials must still authenticate");

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("token").GetString()
            .Should().NotBeNullOrWhiteSpace();
    }

    #endregion

    #region Pin login — suspended business is rejected

    [Fact]
    public async Task PinLogin_With_SuspendedBusiness_Returns_Generic_400()
    {
        var (branchId, pin) = await SeedFreshPinUserAsync(businessActive: false);

        var response = await _client.PostAsJsonAsync(PinLoginRoute, new
        {
            branchId,
            pin
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "a suspended tenant must be rejected at the pin-login boundary");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("suspend",
            "the client message must not leak the suspension reason — info-leak prevention");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Seeds a fresh Business + Owner User pair directly via the DbContext
    /// so each test starts from a known-clean state. Returns the email and
    /// plaintext password the test will use to authenticate.
    /// </summary>
    private async Task<(string Email, string Password)> SeedFreshOwnerAsync(bool businessActive)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var password = "GateTest123!";
        var email = $"gate-{suffix}@example.com";

        var business = new Domain.Models.Business
        {
            Name = $"GateTest-{suffix}",
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = PlanTypeIds.Basic,
            CountryCode = "MX",
            DefaultTaxId = 0,
            TrialEndsAt = null,
            TrialUsed = false,
            OnboardingStatusId = 1,
            CurrentOnboardingStep = 1,
            IsActive = businessActive,
            CreatedAt = DateTime.UtcNow
        };
        db.Businesses.Add(business);
        await db.SaveChangesAsync();

        // Matrix branch — required by ResolveBranchesAsync, which throws
        // "User has no assigned branch" otherwise. The Owner role falls
        // back to any active branch of the business when no UserBranch
        // assignments exist.
        var branch = new Domain.Models.Branch
        {
            BusinessId = business.Id,
            Name = $"Matrix-{suffix}",
            IsMatrix = true,
            IsActive = true,
            FolioCounter = 0,
            TimeZoneId = "America/Mexico_City",
            CreatedAt = DateTime.UtcNow
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var user = new Domain.Models.User
        {
            BusinessId = business.Id,
            Name = $"GateOwner-{suffix}",
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = UserRoleIds.Owner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (email, password);
    }

    /// <summary>
    /// Seeds a Cashier user with a PIN inside a branch of a freshly created
    /// Business. Returns the branchId + plaintext PIN the test will use to
    /// authenticate the PIN login flow.
    /// </summary>
    private async Task<(int BranchId, string Pin)> SeedFreshPinUserAsync(bool businessActive)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var pin = "9999";

        var business = new Domain.Models.Business
        {
            Name = $"GatePin-{suffix}",
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = PlanTypeIds.Basic,
            CountryCode = "MX",
            DefaultTaxId = 0,
            TrialEndsAt = null,
            TrialUsed = false,
            OnboardingStatusId = 1,
            CurrentOnboardingStep = 1,
            IsActive = businessActive,
            CreatedAt = DateTime.UtcNow
        };
        db.Businesses.Add(business);
        await db.SaveChangesAsync();

        var branch = new Domain.Models.Branch
        {
            BusinessId = business.Id,
            Name = $"Branch-{suffix}",
            IsMatrix = true,
            IsActive = true,
            FolioCounter = 0,
            TimeZoneId = "America/Mexico_City",
            CreatedAt = DateTime.UtcNow
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var user = new Domain.Models.User
        {
            BusinessId = business.Id,
            BranchId = branch.Id,
            Name = $"GatePin-{suffix}",
            PinHash = BCrypt.Net.BCrypt.HashPassword(pin),
            RoleId = UserRoleIds.Cashier,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (branch.Id, pin);
    }

    #endregion
}
