using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using POS.IntegrationTests.Infrastructure;
using POS.Services.IService;

namespace POS.IntegrationTests.Catalogs;

/// <summary>
/// Integration suite for the Dynamic Catalogs API (BDD-021 §9.2).
/// Verifies wire shapes, ETag negotiation, Cache-Control headers,
/// auth posture, deterministic sorting, and warm-cache zero-DB behavior.
/// Implements IT-1..IT-12 + IT-12b (13 tests total).
/// </summary>
public class CatalogApiTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string ExpectedCacheControl = "public, max-age=3600, must-revalidate";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CatalogApiTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region Theory Data Sources

    /// <summary>
    /// Enumerates the 11 catalog routes under <c>/api/Catalog/*</c> used by
    /// IT-3 (header emission) and IT-12 (anonymous reachability). Does NOT
    /// include <c>/api/Taxes</c> — that one is authorized and covered
    /// separately by IT-7..IT-10 + IT-12b.
    /// </summary>
    public static IEnumerable<object[]> CatalogRoutes()
    {
        yield return new object[] { "/api/Catalog/kitchen-statuses" };
        yield return new object[] { "/api/Catalog/display-statuses" };
        yield return new object[] { "/api/Catalog/payment-methods" };
        yield return new object[] { "/api/Catalog/device-modes" };
        yield return new object[] { "/api/Catalog/business-types" };
        yield return new object[] { "/api/Catalog/macro-categories" };
        yield return new object[] { "/api/Catalog/zone-types" };
        yield return new object[] { "/api/Catalog/plan-types" };
        yield return new object[] { "/api/Catalog/plans" };
        yield return new object[] { "/api/Catalog/access-reasons" };
        yield return new object[] { "/api/Catalog/access-methods" };
    }

    #endregion

    #region Shape Tests (IT-1, IT-2)

    [Fact]
    public async Task IT_1_GetMacroCategories_Returns_200_With_Four_Rows()
    {
        InvalidateCache("MacroCategories");

        var response = await _client.GetAsync("/api/Catalog/macro-categories");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetArrayLength().Should().Be(4,
            "the seed populates exactly 4 macro categories");

        foreach (var element in document.RootElement.EnumerateArray())
        {
            element.TryGetProperty("id", out _).Should().BeTrue();
            element.TryGetProperty("internalCode", out _).Should().BeTrue();
            element.TryGetProperty("publicName", out _).Should().BeTrue();
            element.TryGetProperty("posExperience", out _).Should().BeTrue();
            element.TryGetProperty("hasKitchen", out _).Should().BeTrue();
            element.TryGetProperty("hasTables", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task IT_2_GetBusinessTypes_Response_Excludes_Navigation_Property()
    {
        InvalidateCache("BusinessTypes");

        var response = await _client.GetAsync("/api/Catalog/business-types");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        foreach (var element in document.RootElement.EnumerateArray())
        {
            element.TryGetProperty("primaryMacroCategoryId", out _).Should().BeTrue(
                "the FK scalar must be projected");
            element.TryGetProperty("primaryMacroCategory", out _).Should().BeFalse(
                "the navigation object must NOT appear in the wire payload");
        }
    }

    #endregion

    #region Headers (IT-3)

    [Theory]
    [MemberData(nameof(CatalogRoutes))]
    public async Task IT_3_EveryCatalogRoute_Emits_ETag_And_CacheControl(string route)
    {
        var response = await _client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"route {route} should return 200");

        response.Headers.ETag.Should().NotBeNull(
            $"route {route} must emit an ETag header");
        response.Headers.ETag!.Tag.Should().StartWith("\"")
            .And.EndWith("\"",
                $"route {route} ETag must be quoted per RFC 9110");
        response.Headers.ETag.Tag.Length.Should().BeGreaterThan(2,
            $"route {route} ETag must contain non-empty content between the quotes");

        // Assert the typed directive set, not the string form — ASP.NET's
        // CacheControlHeaderValue canonicalizes ordering on parse, so a
        // string-equality check against the literal emitted by the server
        // (`public, max-age=3600, must-revalidate`) produces a false negative.
        // RFC 9110 §5.2.1 defines directives as an unordered set.
        var cc = response.Headers.CacheControl;
        cc.Should().NotBeNull($"route {route} must emit a Cache-Control header");
        cc!.Public.Should().BeTrue($"route {route} Cache-Control must carry the `public` directive");
        cc.MustRevalidate.Should().BeTrue($"route {route} Cache-Control must carry the `must-revalidate` directive");
        cc.MaxAge.Should().Be(TimeSpan.FromSeconds(3600),
            $"route {route} Cache-Control must declare max-age=3600");
    }

    #endregion

    #region ETag Negotiation (IT-4, IT-5, IT-6)

    [Fact]
    public async Task IT_4_SecondCall_With_Matching_IfNoneMatch_Returns_304()
    {
        InvalidateCache("MacroCategories");

        var first = await _client.GetAsync("/api/Catalog/macro-categories");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstETag = first.Headers.ETag!.Tag;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Catalog/macro-categories");
        request.Headers.TryAddWithoutValidation("If-None-Match", firstETag);
        var second = await _client.SendAsync(request);

        second.StatusCode.Should().Be(HttpStatusCode.NotModified);

        var body = await second.Content.ReadAsByteArrayAsync();
        body.Length.Should().Be(0, "304 responses must carry no body");

        second.Headers.ETag.Should().NotBeNull("304 must preserve the ETag header");
        second.Headers.ETag!.Tag.Should().Be(firstETag);

        // Typed directive assertion — see IT-3 rationale.
        var cc = second.Headers.CacheControl;
        cc.Should().NotBeNull("304 must preserve the Cache-Control header");
        cc!.Public.Should().BeTrue();
        cc.MustRevalidate.Should().BeTrue();
        cc.MaxAge.Should().Be(TimeSpan.FromSeconds(3600));
    }

    [Fact]
    public async Task IT_5_SecondCall_With_Stale_IfNoneMatch_Returns_200_And_Fresh_ETag()
    {
        InvalidateCache("MacroCategories");

        var first = await _client.GetAsync("/api/Catalog/macro-categories");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var currentETag = first.Headers.ETag!.Tag;

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/Catalog/macro-categories");
        request.Headers.TryAddWithoutValidation("If-None-Match", "\"stale-fingerprint-value\"");
        var second = await _client.SendAsync(request);

        second.StatusCode.Should().Be(HttpStatusCode.OK,
            "a mismatched If-None-Match must NOT short-circuit to 304");
        var body = await second.Content.ReadAsStringAsync();
        body.Should().NotBeNullOrEmpty("200 must carry the body");

        second.Headers.ETag.Should().NotBeNull();
        second.Headers.ETag!.Tag.Should().Be(currentETag,
            "the response ETag must reflect the server's current fingerprint, unchanged by client request");
    }

    [Fact]
    public async Task IT_6_TwoCalls_Without_IfNoneMatch_Produce_Same_ETag()
    {
        InvalidateCache("MacroCategories");

        var first = await _client.GetAsync("/api/Catalog/macro-categories");
        var second = await _client.GetAsync("/api/Catalog/macro-categories");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        first.Headers.ETag.Should().NotBeNull();
        second.Headers.ETag.Should().NotBeNull();
        second.Headers.ETag!.Tag.Should().Be(first.Headers.ETag!.Tag,
            "ETag must be deterministic across calls with stable underlying data");
    }

    #endregion

    #region Taxes (IT-7, IT-8, IT-9, IT-10)

    [Fact]
    public async Task IT_7_GetTaxes_With_Bearer_Returns_All_Rows()
    {
        using var authorized = CreateAuthorizedClient();
        var response = await authorized.GetAsync("/api/Taxes");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetArrayLength().Should().BeGreaterThan(0,
            "the tax seed populates at least one row");
    }

    [Fact]
    public async Task IT_8_GetTaxes_With_Bearer_And_MX_Returns_Only_MX()
    {
        using var authorized = CreateAuthorizedClient();
        var response = await authorized.GetAsync("/api/Taxes?countryCode=MX");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetArrayLength().Should().BeGreaterThan(0,
            "the MX-filtered query must return seeded MX tax rows");

        foreach (var element in document.RootElement.EnumerateArray())
        {
            element.GetProperty("countryCode").GetString()
                .Should().Be("MX",
                    "every returned row must satisfy the country filter");
        }
    }

    [Fact]
    public async Task IT_9_GetTaxes_With_Bearer_And_Unknown_Country_Returns_Empty_Array()
    {
        using var authorized = CreateAuthorizedClient();
        var response = await authorized.GetAsync("/api/Taxes?countryCode=XX");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "unknown country codes must yield 200 + empty array, never 404");

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task IT_10_GetTaxes_With_Invalid_CountryCode_Returns_400()
    {
        using var authorized = CreateAuthorizedClient();
        var response = await authorized.GetAsync("/api/Taxes?countryCode=MXX");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(
            "El parámetro 'countryCode' debe ser un código ISO 3166-1 alpha-2 de 2 letras.",
            "VR-001 Spanish message must be carried verbatim in the response body");
    }

    #endregion

    #region Counter (IT-11)

    [Fact]
    public async Task IT_11_WarmCache_Triggers_Zero_Materializations()
    {
        InvalidateCache("MacroCategories");
        _factory.QueryCounter.Reset();

        // First call — cold cache → repo hit → entities materialize.
        var firstResponse = await _client.GetAsync("/api/Catalog/macro-categories");
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstDelta = _factory.QueryCounter.Count;
        firstDelta.Should().BeGreaterOrEqualTo(1,
            "the first call after Invalidate must materialize at least one entity from the repository");

        // Second call — warm cache → service short-circuits, no materialization.
        var beforeSecond = _factory.QueryCounter.Count;
        var secondResponse = await _client.GetAsync("/api/Catalog/macro-categories");
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondDelta = _factory.QueryCounter.Count - beforeSecond;
        secondDelta.Should().Be(0,
            "the second call must be served from IMemoryCache with zero materializations");
    }

    #endregion

    #region Auth (IT-12, IT-12b)

    [Theory]
    [MemberData(nameof(CatalogRoutes))]
    public async Task IT_12_EveryCatalogRoute_Reachable_Anonymously(string route)
    {
        // No Authorization header on the default client.
        var response = await _client.GetAsync(route);
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"{route} must remain [AllowAnonymous]");
    }

    [Fact]
    public async Task IT_12b_GetTaxes_Without_Bearer_Returns_401()
    {
        var response = await _client.GetAsync("/api/Taxes");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "/api/Taxes must preserve its [Authorize] posture");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Clears a single catalog cache entry so the next request rebuilds it.
    /// Uses the PascalCase resource names defined in BDD-021 §6.1.D.
    /// </summary>
    private void InvalidateCache(string resourceName)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        service.Invalidate(resourceName);
    }

    /// <summary>
    /// Builds an HttpClient carrying a valid Bearer token for tenant A
    /// (Business 1 / Branch 1, Owner role) — used by /api/Taxes tests.
    /// </summary>
    private HttpClient CreateAuthorizedClient()
    {
        var client = _factory.CreateClient();
        var token = JwtTestFactory.CreateUserToken(
            businessId: TestConstants.BusinessAId,
            branchId: TestConstants.BranchAId,
            userId: TestConstants.UserAId,
            role: "Owner");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    #endregion
}
