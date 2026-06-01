using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Users;

/// <summary>
/// Integration coverage for <c>POST /api/User/welcome-shown</c>. Verifies
/// the auth posture (device-token rejection, anonymous rejection),
/// the first-time set behavior, and the idempotency guarantee — repeat
/// calls preserve the original timestamp rather than overwriting it.
/// </summary>
public class WelcomeShownTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Route = "/api/User/welcome-shown";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _anonymousClient;

    public WelcomeShownTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _anonymousClient = factory.CreateClient();
    }

    #region Test 1 — Anonymous → 401

    [Fact]
    public async Task IT_WSH_1_Anonymous_Request_Returns_401()
    {
        var response = await _anonymousClient.PostAsync(Route, content: null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Test 2 — Device token → 401 (no user identity to mark)

    [Fact]
    public async Task IT_WSH_2_Device_Token_Returns_401()
    {
        var client = _factory.CreateClient();
        var deviceToken = JwtTestFactory.CreateDeviceToken(
            businessId: TestConstants.BusinessAId,
            branchId: TestConstants.BranchAId,
            mode: "kds",
            features: Array.Empty<string>());
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", deviceToken);

        var response = await client.PostAsync(Route, content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "device tokens have no user identity — WelcomeShownAt lives on User");
    }

    #endregion

    #region Test 3 — First call sets timestamp + JWT carries claim

    [Fact]
    public async Task IT_WSH_3_First_Call_Sets_Timestamp_And_Emits_WelcomeShownAt_Claim()
    {
        // Use a fresh user so the test does not depend on the order in
        // which other suites mutate the shared seed user.
        var userId = await CreateFreshOwnerAsync();
        var client = _factory.CreateClient();
        var token = JwtTestFactory.CreateUserToken(
            businessId: TestConstants.BusinessAId,
            branchId: TestConstants.BranchAId,
            userId: userId,
            role: "Owner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsync(Route, content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        var welcomeShownAt = document.RootElement.GetProperty("welcomeShownAt").GetString();
        welcomeShownAt.Should().NotBeNullOrEmpty(
            "the AuthResponse must surface the freshly-set timestamp");

        var newJwt = document.RootElement.GetProperty("token").GetString();
        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(newJwt);
        var claim = parsed.Claims.FirstOrDefault(c => c.Type == "welcomeShownAt");
        claim.Should().NotBeNull();
        claim!.Value.Should().NotBeEmpty(
            "the regenerated JWT must carry the welcomeShownAt claim populated");

        // Verify persistence — the column was actually written, not just
        // surfaced in the response.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await db.Users
            .IgnoreQueryFilters()
            .FirstAsync(u => u.Id == userId);
        user.WelcomeShownAt.Should().NotBeNull();
    }

    #endregion

    #region Test 4 — Second call preserves the original timestamp (idempotency)

    [Fact]
    public async Task IT_WSH_4_Second_Call_Preserves_First_Seen_Timestamp()
    {
        var userId = await CreateFreshOwnerAsync();
        var client = _factory.CreateClient();
        var token = JwtTestFactory.CreateUserToken(
            businessId: TestConstants.BusinessAId,
            branchId: TestConstants.BranchAId,
            userId: userId,
            role: "Owner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var first = await client.PostAsync(Route, content: null);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        var firstTimestamp = firstBody.RootElement.GetProperty("welcomeShownAt").GetString();

        // Brief delay so we can distinguish UTC values when the second
        // write would (incorrectly) override the first.
        await Task.Delay(20);

        var second = await client.PostAsync(Route, content: null);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        var secondTimestamp = secondBody.RootElement.GetProperty("welcomeShownAt").GetString();

        secondTimestamp.Should().Be(firstTimestamp,
            "the second call must preserve the first-seen timestamp — semantics is " +
            "\"when did the user first dismiss welcome\", not \"last dismissal\"");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Inserts a fresh Owner User in the seeded test tenant so each test
    /// gets an independent <c>WelcomeShownAt = null</c> starting point.
    /// Returns the new user id.
    /// </summary>
    private async Task<int> CreateFreshOwnerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = new Domain.Models.User
        {
            BusinessId = TestConstants.BusinessAId,
            BranchId = TestConstants.BranchAId,
            Name = $"Welcome-Test-{Guid.NewGuid():N}".Substring(0, 30),
            RoleId = Domain.Helpers.UserRoleIds.Owner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    #endregion
}
