using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using POS.IntegrationTests.Infrastructure;
using POS.Services.IService;

namespace POS.IntegrationTests.Catalogs;

/// <summary>
/// Integration coverage for the BusinessTypeCatalog expansion (20 → 123
/// sub-giros) and the ops-only admin catalog invalidation endpoint.
/// Verifies the post-seed row count, ClusterCode emission on Services
/// entries (and its omission elsewhere), cross-macro acceptance on
/// <c>PUT /api/Business/giro</c>, and the auth / shape / cache-eviction
/// contract of <c>POST /api/Admin/catalogs/invalidate</c>.
/// </summary>
public class CatalogExpansionTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminInvalidateRoute = "/api/Admin/catalogs/invalidate";
    private const string AdminTokenHeader = "X-Admin-Token";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public CatalogExpansionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region IT-EXP-1 — Seed count

    [Fact]
    public async Task IT_EXP_1_BusinessTypes_Returns_123_Rows_With_Expected_Macro_Distribution()
    {
        InvalidateCache("BusinessTypes");

        var response = await _client.GetAsync("/api/Catalog/business-types");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var entries = document.RootElement.EnumerateArray().ToList();

        entries.Count.Should().Be(123,
            "the expansion seeds the full taxonomy of 20 existing + 103 new sub-giros");

        var byMacro = entries
            .GroupBy(e => e.GetProperty("primaryMacroCategoryId").GetInt32())
            .ToDictionary(g => g.Key, g => g.Count());

        byMacro[1].Should().Be(15, "Macro 1 (Restaurantes) → 3 existing + 12 new = 15");
        byMacro[2].Should().Be(18, "Macro 2 (Comida Rápida) → 6 existing + 12 new = 18");
        byMacro[3].Should().Be(24, "Macro 3 (Retail) → 7 existing + 17 new = 24");
        byMacro[4].Should().Be(66, "Macro 4 (Services) → 4 existing + 62 new = 66");
    }

    #endregion

    #region IT-EXP-2 — Cross-macro PUT /Business/giro

    [Fact]
    public async Task IT_EXP_2_UpdateGiro_Accepts_CrossMacro_SubGiros()
    {
        var client = CreateAuthorizedClient(role: "Owner");

        // SubGiro 24 = "Salón de uñas" (Macro 4, cluster=beauty); SubGiro 16 =
        // "Boutique / Ropa y Calzado" (Macro 3). PrimaryMacro=4 with one
        // foreign-macro id verifies the validator stays permissive — the FE
        // cluster-driven UX surfaces hybrids like salons-that-also-sell-product.
        var request = new
        {
            primaryMacroCategoryId = 4,
            subGiroIds = new[] { 24, 16 },
            customGiroDescription = (string?)null
        };

        var response = await client.PutAsJsonAsync("/api/Business/giro", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "cross-macro selections must round-trip without rejection");

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("primaryMacroCategoryId").GetInt32().Should().Be(4);

        var subGiros = document.RootElement.GetProperty("subGiroIds")
            .EnumerateArray()
            .Select(e => e.GetInt32())
            .ToList();
        subGiros.Should().BeEquivalentTo(new[] { 24, 16 });
    }

    #endregion

    #region IT-EXP-3 — ClusterCode emission

    [Fact]
    public async Task IT_EXP_3_BusinessTypes_Emit_ClusterCode_Only_For_Services_Macro()
    {
        InvalidateCache("BusinessTypes");

        var response = await _client.GetAsync("/api/Catalog/business-types");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        var validClusters = new HashSet<string>
        {
            "beauty", "health", "automotive", "pets", "repair",
            "fitness", "education", "home", "events", "professional"
        };

        foreach (var element in document.RootElement.EnumerateArray())
        {
            var macroId = element.GetProperty("primaryMacroCategoryId").GetInt32();
            var hasCluster = element.TryGetProperty("clusterCode", out var clusterElement);

            if (macroId == 4)
            {
                hasCluster.Should().BeTrue(
                    "every Services (macro=4) entry must carry a clusterCode");
                clusterElement.ValueKind.Should().Be(JsonValueKind.String);
                validClusters.Should().Contain(clusterElement.GetString()!,
                    "the clusterCode must be one of the 10 canonical slugs");
            }
            else
            {
                // WhenWritingNull omits the property entirely for non-Services
                // rows — the JSON shape is { id, primaryMacroCategoryId, name }.
                hasCluster.Should().BeFalse(
                    $"non-Services entries (macro={macroId}) must omit clusterCode under WhenWritingNull");
            }
        }
    }

    #endregion

    #region IT-EXP-4 — Auth (no header)

    [Fact]
    public async Task IT_EXP_4_AdminInvalidate_Without_AdminToken_Returns_401()
    {
        using var request = BuildInvalidateRequest(new[] { "BusinessTypes" });
        // No X-Admin-Token header.
        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the admin scheme must reject requests missing X-Admin-Token");
    }

    #endregion

    #region IT-EXP-5 — Auth (valid header) + cache miss verification

    [Fact]
    public async Task IT_EXP_5_AdminInvalidate_With_Valid_Token_Returns_204_And_Forces_Cache_Miss()
    {
        // 1. Warm the cache so a subsequent read would otherwise be a hit.
        InvalidateCache("BusinessTypes");
        var warmUp = await _client.GetAsync("/api/Catalog/business-types");
        warmUp.StatusCode.Should().Be(HttpStatusCode.OK);

        var beforeSecondGet = _factory.QueryCounter.Count;
        var hitResponse = await _client.GetAsync("/api/Catalog/business-types");
        hitResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var warmDelta = _factory.QueryCounter.Count - beforeSecondGet;
        warmDelta.Should().Be(0,
            "pre-condition: second GET before invalidate must be served from cache");

        // 2. Invalidate via the admin endpoint.
        using var invalidateRequest = BuildInvalidateRequest(new[] { "BusinessTypes" });
        invalidateRequest.Headers.Add(AdminTokenHeader, TestConstants.AdminApiToken);
        var invalidate = await _client.SendAsync(invalidateRequest);

        invalidate.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "valid admin token + whitelisted key must produce 204");

        // 3. The next GET must repopulate the cache from the database.
        var beforePostInvalidate = _factory.QueryCounter.Count;
        var postInvalidate = await _client.GetAsync("/api/Catalog/business-types");
        postInvalidate.StatusCode.Should().Be(HttpStatusCode.OK);
        var coldDelta = _factory.QueryCounter.Count - beforePostInvalidate;
        coldDelta.Should().BeGreaterOrEqualTo(1,
            "the GET after invalidate must materialize at least one row from the repository");
    }

    #endregion

    #region IT-EXP-6 — Unknown key (400)

    [Fact]
    public async Task IT_EXP_6_AdminInvalidate_With_Unknown_CatalogKey_Returns_400()
    {
        using var request = BuildInvalidateRequest(new[] { "BusinessTypes", "NotARealCatalog" });
        request.Headers.Add(AdminTokenHeader, TestConstants.AdminApiToken);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            "any unknown key in the payload must reject the entire request");

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.TryGetProperty("invalidKeys", out var invalidKeys).Should().BeTrue();

        var keys = invalidKeys.EnumerateArray()
            .Select(e => e.GetString())
            .ToList();
        keys.Should().Contain("NotARealCatalog");
        keys.Should().NotContain("BusinessTypes",
            "valid keys must not appear in the invalidKeys diagnostic");
    }

    #endregion

    #region Helpers

    private void InvalidateCache(string resourceName)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        service.Invalidate(resourceName);
        _factory.QueryCounter.Reset();
    }

    private static HttpRequestMessage BuildInvalidateRequest(IEnumerable<string> catalogKeys)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, AdminInvalidateRoute)
        {
            Content = JsonContent.Create(new { catalogKeys })
        };
        return message;
    }

    private HttpClient CreateAuthorizedClient(string role)
    {
        var client = _factory.CreateClient();
        var token = JwtTestFactory.CreateUserToken(
            businessId: TestConstants.BusinessAId,
            branchId: TestConstants.BranchAId,
            userId: TestConstants.UserAId,
            role: role);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    #endregion
}
