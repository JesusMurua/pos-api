using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Moq;
using POS.Domain.DTOs.Admin;
using POS.IntegrationTests.Infrastructure;
using POS.Services.IService;

namespace POS.IntegrationTests.Admin;

/// <summary>
/// Integration coverage for <c>/api/Admin/businesses</c>. Verifies the
/// auth posture, the <see cref="AdminCreateBusinessRequest.IncludeOwnerJwt"/>
/// opt-in, welcome email suppression semantics, duplicate handling, and
/// the cross-tenant directory listing (bypasses BDD-019 query filters).
/// </summary>
public class AdminBusinessesControllerTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminRoute = "/api/Admin/businesses";
    private const string AdminTokenHeader = "X-Admin-Token";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _anonymousClient;

    public AdminBusinessesControllerTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _anonymousClient = factory.CreateClient();
    }

    #region Test 1 — POST without admin token

    [Fact]
    public async Task IT_ADM_BIZ_1_Create_Without_AdminToken_Returns_401()
    {
        var request = BuildCreateRequest(NewUniqueSuffix());

        var response = await _anonymousClient.PostAsJsonAsync(AdminRoute, request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the admin scheme must reject requests missing X-Admin-Token");
    }

    #endregion

    #region Test 2 — POST default (IncludeOwnerJwt omitted) → OwnerJwt omitted from response

    [Fact]
    public async Task IT_ADM_BIZ_2_Create_With_Default_Flags_Omits_OwnerJwt_From_Response()
    {
        var client = CreateAdminClient();
        var request = BuildCreateRequest(NewUniqueSuffix());
        // IncludeOwnerJwt defaults to false via the DTO property initializer.

        var response = await client.PostAsJsonAsync(AdminRoute, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        document.RootElement.TryGetProperty("ownerJwt", out _).Should().BeFalse(
            "with IncludeOwnerJwt=false the field must be omitted under the global WhenWritingNull policy");
        document.RootElement.GetProperty("businessId").GetInt32().Should().BeGreaterThan(0);
        document.RootElement.GetProperty("planTypeCode").GetString().Should().Be("Basic");
        document.RootElement.GetProperty("primaryMacroCategoryCode").GetString().Should().Be("services");
    }

    #endregion

    #region Test 3 — POST with IncludeOwnerJwt=true → response carries a decodable JWT

    [Fact]
    public async Task IT_ADM_BIZ_3_Create_With_IncludeOwnerJwt_True_Returns_Decodable_Token()
    {
        var client = CreateAdminClient();
        var request = BuildCreateRequest(NewUniqueSuffix()) with { IncludeOwnerJwt = true };

        var response = await client.PostAsJsonAsync(AdminRoute, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        document.RootElement.TryGetProperty("ownerJwt", out var jwtElement).Should().BeTrue(
            "with IncludeOwnerJwt=true the field must be present");
        var jwt = jwtElement.GetString();
        jwt.Should().NotBeNullOrWhiteSpace();

        // Round-trip through the parser to confirm structural integrity; the
        // claim contents belong to AuthService and are covered by its own tests.
        var handler = new JwtSecurityTokenHandler();
        handler.CanReadToken(jwt).Should().BeTrue("the returned string must be a parseable JWT");

        var parsed = handler.ReadJwtToken(jwt);
        parsed.Claims.Should().Contain(c => c.Type == "businessId");
    }

    #endregion

    #region Test 4 — POST with duplicate email returns 409

    [Fact]
    public async Task IT_ADM_BIZ_4_Create_With_Duplicate_Email_Returns_409()
    {
        var client = CreateAdminClient();
        var first = BuildCreateRequest(NewUniqueSuffix());

        var initial = await client.PostAsJsonAsync(AdminRoute, first);
        initial.StatusCode.Should().Be(HttpStatusCode.OK, "first create must succeed");

        var duplicate = BuildCreateRequest(NewUniqueSuffix()) with { Email = first.Email };
        var conflict = await client.PostAsJsonAsync(AdminRoute, duplicate);

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict,
            "re-using an email must surface as 409 — same contract as the public Register controller");
    }

    #endregion

    #region Test 5 — POST default (SuppressWelcomeEmail=true) skips the welcome email

    [Fact]
    public async Task IT_ADM_BIZ_5_Default_Suppress_Skips_WelcomeEmail()
    {
        _factory.EmailServiceMock.Invocations.Clear();

        var client = CreateAdminClient();
        var request = BuildCreateRequest(NewUniqueSuffix());
        // SuppressWelcomeEmail defaults to true via the DTO property initializer.

        var response = await client.PostAsJsonAsync(AdminRoute, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.EmailServiceMock.Verify(
            x => x.SendWelcomeEmailAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never(),
            "the admin endpoint default must NOT dispatch the welcome email");
    }

    #endregion

    #region Test 6 — POST with SuppressWelcomeEmail=false sends the welcome email

    [Fact]
    public async Task IT_ADM_BIZ_6_Explicit_Suppress_False_Sends_WelcomeEmail()
    {
        _factory.EmailServiceMock.Invocations.Clear();

        var client = CreateAdminClient();
        var request = BuildCreateRequest(NewUniqueSuffix()) with { SuppressWelcomeEmail = false };

        var response = await client.PostAsJsonAsync(AdminRoute, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        _factory.EmailServiceMock.Verify(
            x => x.SendWelcomeEmailAsync(
                request.Email, request.OwnerName, request.BusinessName),
            Times.Once(),
            "the admin endpoint must dispatch the welcome email when SuppressWelcomeEmail=false");
    }

    #endregion

    #region Test 7 — GET without admin token

    [Fact]
    public async Task IT_ADM_BIZ_7_List_Without_AdminToken_Returns_401()
    {
        var response = await _anonymousClient.GetAsync(AdminRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Test 8 — GET filtered by search lists exactly the 3 created businesses

    [Fact]
    public async Task IT_ADM_BIZ_8_List_With_Search_Filter_Lists_Created_Businesses_Cross_Tenant()
    {
        var client = CreateAdminClient();

        // Bucket prefix — unique per test run so the search filter isolates
        // these three rows from any other AdminTest- businesses left by
        // previous tests in the shared InMemory database.
        var bucket = $"AdminTest-{Guid.NewGuid():N}-";
        var createdNames = new List<string>();

        for (var i = 0; i < 3; i++)
        {
            var suffix = NewUniqueSuffix();
            var request = BuildCreateRequest(suffix) with { BusinessName = $"{bucket}{i}" };
            var post = await client.PostAsJsonAsync(AdminRoute, request);
            post.StatusCode.Should().Be(HttpStatusCode.OK, $"create {i} must succeed");
            createdNames.Add(request.BusinessName);
        }

        var listResponse = await client.GetAsync(
            $"{AdminRoute}?search={Uri.EscapeDataString(bucket)}");
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await listResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        var totalCount = document.RootElement.GetProperty("totalCount").GetInt32();
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();

        totalCount.Should().Be(3,
            "the search filter isolates the 3 freshly-created rows; the BDD-019 query filter " +
            "is bypassed via IgnoreQueryFilters() in the admin repo method");
        items.Should().HaveCount(3);

        var returnedNames = items
            .Select(e => e.GetProperty("name").GetString())
            .ToList();

        returnedNames.Should().BeEquivalentTo(createdNames,
            "the directory must surface every business whose name matches the search prefix");
    }

    #endregion

    #region Helpers

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminTokenHeader, TestConstants.AdminApiToken);
        return client;
    }

    private static string NewUniqueSuffix() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Constructs a syntactically valid <see cref="AdminCreateBusinessRequest"/>
    /// with per-test uniqueness baked into the email and business name so the
    /// shared InMemory database does not produce cross-test 409s. Defaults:
    /// macro = Services (4), plan = Basic (2). Mutate via <c>with</c>
    /// expressions in individual tests when a specific shape is needed.
    /// </summary>
    private static AdminCreateBusinessRequest BuildCreateRequest(string suffix) =>
        new()
        {
            BusinessName = $"AdminTest-{suffix}",
            OwnerName = $"Owner {suffix}",
            Email = $"admin-test-{suffix}@example.com",
            Password = "AdminPass123!",
            PrimaryMacroCategoryId = 4,
            PlanTypeId = 2,
            CountryCode = "MX"
            // SuppressWelcomeEmail and IncludeOwnerJwt left at their DTO defaults
            // (true / false respectively); individual tests override via `with`.
        };

    #endregion
}
