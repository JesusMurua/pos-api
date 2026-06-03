using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Domain.Models.Catalogs;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;
using POS.Services.IService;

namespace POS.IntegrationTests.Features;

/// <summary>
/// Coverage for the admin feature-matrix endpoints: X-Admin-Token auth, bulk
/// upsert-merge, presence add/remove, override CRUD, preview-impact, audit log,
/// bootstrap-only seed survival, and immediate effect via InvalidateAll.
/// Mutating tests use cluster/feature pairs no other test touches and clean up.
/// </summary>
public class AdminFeatureMatrixTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminTokenHeader = "X-Admin-Token";
    private const int RealtimeAccessControl = (int)POS.Domain.Enums.FeatureKey.RealtimeAccessControl; // 120
    private const int MaxKiosks = (int)POS.Domain.Enums.FeatureKey.MaxKiosks; // 15

    private readonly CustomWebApplicationFactory _factory;

    public AdminFeatureMatrixTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task NoAdminToken_Returns401()
    {
        var anon = _factory.CreateClient();
        (await anon.GetAsync("/api/Admin/feature-catalog")).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetFeatureCatalog_Returns_SeededFeatures()
    {
        var client = AdminClient();
        var response = await client.GetAsync("/api/Admin/feature-catalog");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadJsonAsync(response);
        body.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("key").GetString())
            .Should().Contain("RealtimeAccessControl");
    }

    [Fact]
    public async Task PutPlanMatrix_InvalidFeatureId_Returns400()
    {
        var client = AdminClient();
        var response = await client.PutAsJsonAsync("/api/Admin/plan-feature-matrix", new[]
        {
            new { planTypeId = PlanTypeIds.Pro, featureId = 999999, isEnabled = true, defaultLimit = (int?)null }
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PutPlanMatrix_EmptyArray_NoOp_Returns204()
    {
        var client = AdminClient();
        var response = await client.PutAsJsonAsync("/api/Admin/plan-feature-matrix", Array.Empty<object>());
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ClusterMatrix_Presence_AddThenRemove()
    {
        var client = AdminClient();

        // Add (home, MaxKiosks) — a pair no other test or seed uses.
        (await client.PutAsJsonAsync("/api/Admin/cluster-feature-matrix", new[]
        {
            new { clusterCode = ClusterCodes.Home, featureId = MaxKiosks, isApplicable = true }
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ClusterRowsAsync(client)).Should().Contain((ClusterCodes.Home, MaxKiosks));

        // Remove it.
        (await client.PutAsJsonAsync("/api/Admin/cluster-feature-matrix", new[]
        {
            new { clusterCode = ClusterCodes.Home, featureId = MaxKiosks, isApplicable = false }
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ClusterRowsAsync(client)).Should().NotContain((ClusterCodes.Home, MaxKiosks));
    }

    [Fact]
    public async Task Override_Create_Get_Delete()
    {
        var client = AdminClient();
        var dto = new { planTypeId = PlanTypeIds.Free, macroCategoryId = MacroCategoryIds.Retail, featureId = MaxKiosks, isEnabled = true };

        (await client.PostAsJsonAsync("/api/Admin/plan-business-type-overrides", dto))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterCreate = await ReadJsonAsync(await client.GetAsync("/api/Admin/plan-business-type-overrides"));
        afterCreate.RootElement.EnumerateArray()
            .Any(o => o.GetProperty("planTypeId").GetInt32() == PlanTypeIds.Free
                   && o.GetProperty("featureId").GetInt32() == MaxKiosks)
            .Should().BeTrue();

        (await client.DeleteAsync($"/api/Admin/plan-business-type-overrides/{PlanTypeIds.Free}/{MacroCategoryIds.Retail}/{MaxKiosks}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDelete = await ReadJsonAsync(await client.GetAsync("/api/Admin/plan-business-type-overrides"));
        afterDelete.RootElement.EnumerateArray()
            .Any(o => o.GetProperty("planTypeId").GetInt32() == PlanTypeIds.Free
                   && o.GetProperty("featureId").GetInt32() == MaxKiosks)
            .Should().BeFalse();
    }

    [Fact]
    public async Task PreviewImpact_Cluster_Counts_EffectivelyAffected()
    {
        // A 'repair' Services/Pro business: RealtimeAccessControl is OFF (only the
        // fitness cluster gates it). Previewing ADD of the repair cluster flips it ON.
        await SeedServicesBusinessAsync(ClusterCodes.Repair, PlanTypeIds.Pro);
        var client = AdminClient();

        var response = await client.GetAsync(
            $"/api/Admin/feature-matrix/preview-impact?axis=cluster&clusterCode={ClusterCodes.Repair}&featureId={RealtimeAccessControl}&isApplicable=true");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("affectedCount").GetInt32().Should().BeGreaterThan(0);
        body.RootElement.GetProperty("breakdownByPlan").GetProperty("pro").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task PreviewImpact_NonClusterAxis_Returns400()
    {
        var client = AdminClient();
        (await client.GetAsync($"/api/Admin/feature-matrix/preview-impact?axis=plan&clusterCode=fitness&featureId={RealtimeAccessControl}&isApplicable=false"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task AuditLog_RecordsMutations_DescOrder_WithTokenId()
    {
        var client = AdminClient();

        // Two distinct mutations on the same key, newest last (add then remove).
        await client.PutAsJsonAsync("/api/Admin/cluster-feature-matrix", new[]
        { new { clusterCode = ClusterCodes.Events, featureId = MaxKiosks, isApplicable = true } });
        await client.PutAsJsonAsync("/api/Admin/cluster-feature-matrix", new[]
        { new { clusterCode = ClusterCodes.Events, featureId = MaxKiosks, isApplicable = false } });

        var body = await ReadJsonAsync(await client.GetAsync("/api/Admin/feature-matrix/audit-log?axis=cluster&pageSize=200"));
        var all = body.RootElement.GetProperty("items").EnumerateArray().ToList();

        // Every cluster audit row carries the token attribution.
        all.Should().OnlyContain(e => e.GetProperty("axis").GetString() == "cluster");
        all[0].GetProperty("changedByTokenId").GetString().Should().NotBeNullOrEmpty(
            "attribution is by the admin token's token_id claim");

        // The two rows for my key, in DESC recency: the remove (no afterJson) is newer than the add.
        // Null JSON properties are omitted from the response (WhenWritingNull).
        static bool IsRemove(JsonElement e) =>
            !e.TryGetProperty("afterJson", out var a) || a.ValueKind == JsonValueKind.Null;

        var mine = all.Select((e, i) => (e, i))
            .Where(x => x.e.GetProperty("entityKey").GetString() == $"cluster=events;feature={MaxKiosks}")
            .ToList();
        mine.Should().HaveCountGreaterThanOrEqualTo(2);
        var removeIndex = mine.First(x => IsRemove(x.e)).i;
        var addIndex = mine.First(x => !IsRemove(x.e)).i;
        removeIndex.Should().BeLessThan(addIndex, "newest-first: the remove came after the add");
    }

    [Fact]
    public async Task Seed_BootstrapOnly_AdminEdit_Survives_ReSeed()
    {
        var client = AdminClient();

        // Edit (Free, MaxKiosks): seeded IsEnabled=true, limit=0 → set false/99.
        await client.PutAsJsonAsync("/api/Admin/plan-feature-matrix", new[]
        {
            new { planTypeId = PlanTypeIds.Free, featureId = MaxKiosks, isEnabled = false, defaultLimit = (int?)99 }
        });

        // Re-run the bootstrap seed.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DbInitializer.SeedSystemDataAsync(db);
        }

        // The admin edit must survive — bootstrap-only never overwrites existing rows.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.PlanFeatureMatrices
                .FirstAsync(r => r.PlanTypeId == PlanTypeIds.Free && r.FeatureId == MaxKiosks);
            row.IsEnabled.Should().BeFalse("re-seed must not revert the admin edit");
            row.DefaultLimit.Should().Be(99);
        }
    }

    [Fact]
    public async Task EndpointPut_TakesEffect_Immediately_ViaInvalidateAll()
    {
        // A 'professional' Services/Pro business — AppointmentReminders already ON
        // for it, but RealtimeAccessControl OFF (not a gym cluster).
        var businessId = await SeedServicesBusinessAsync(ClusterCodes.Professional, PlanTypeIds.Pro);
        var client = AdminClient();

        (await ResolveAsync(businessId)).Should().NotContain("RealtimeAccessControl");

        // Make access control applicable to 'professional' via the endpoint.
        await client.PutAsJsonAsync("/api/Admin/cluster-feature-matrix", new[]
        { new { clusterCode = ClusterCodes.Professional, featureId = RealtimeAccessControl, isApplicable = true } });

        (await ResolveAsync(businessId)).Should().Contain("RealtimeAccessControl",
            "the PUT calls InvalidateAll, so the change is visible immediately");

        // Cleanup so the rule does not leak to other tests.
        await client.PutAsJsonAsync("/api/Admin/cluster-feature-matrix", new[]
        { new { clusterCode = ClusterCodes.Professional, featureId = RealtimeAccessControl, isApplicable = false } });
    }

    #region Helpers

    private HttpClient AdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminTokenHeader, TestConstants.AdminApiToken);
        return client;
    }

    private async Task<List<(string Cluster, int Feature)>> ClusterRowsAsync(HttpClient client)
    {
        var body = await ReadJsonAsync(await client.GetAsync("/api/Admin/cluster-feature-matrix"));
        return body.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("clusterCode").GetString()!, r.GetProperty("featureId").GetInt32()))
            .ToList();
    }

    private async Task<IReadOnlyList<string>> ResolveAsync(int businessId)
    {
        using var scope = _factory.Services.CreateScope();
        var gate = scope.ServiceProvider.GetRequiredService<IFeatureGateService>();
        return await gate.GetEnabledFeaturesAsync(businessId);
    }

    private async Task<int> SeedServicesBusinessAsync(string clusterCode, int planTypeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Business
        {
            Name = $"AdminTest-{suffix}",
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = planTypeId,
            CountryCode = "MX",
            DefaultTaxId = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();

        var catalog = new BusinessTypeCatalog
        {
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            Name = $"{clusterCode}-{suffix}",
            ClusterCode = clusterCode
        };
        db.Set<BusinessTypeCatalog>().Add(catalog);
        await db.SaveChangesAsync();

        db.Set<BusinessGiro>().Add(new BusinessGiro { BusinessId = biz.Id, BusinessTypeId = catalog.Id });
        await db.SaveChangesAsync();
        return biz.Id;
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var stream = await response.Content.ReadAsStreamAsync();
        return await JsonDocument.ParseAsync(stream);
    }

    #endregion
}
