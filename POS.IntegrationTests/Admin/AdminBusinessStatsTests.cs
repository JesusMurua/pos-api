using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.DTOs.Admin;
using POS.Domain.Helpers;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Admin;

/// <summary>
/// Integration coverage for <c>GET /api/Admin/businesses/stats</c>.
/// Verifies the admin auth posture, baseline shape on the seeded DB,
/// plan / macro distribution arithmetic over freshly-created businesses,
/// the trial-window semantics (active half-open intervals), and the
/// six-bucket calendar backfill of the <c>CreatedByMonth</c> series.
/// </summary>
public class AdminBusinessStatsTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string StatsRoute = "/api/Admin/businesses/stats";
    private const string AdminTokenHeader = "X-Admin-Token";

    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _anonymousClient;

    public AdminBusinessStatsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _anonymousClient = factory.CreateClient();
    }

    #region Test 1 — Anonymous → 401

    [Fact]
    public async Task IT_STATS_1_Stats_Without_AdminToken_Returns_401()
    {
        var response = await _anonymousClient.GetAsync(StatsRoute);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    #endregion

    #region Test 2 — Fresh DB baseline

    [Fact]
    public async Task IT_STATS_2_Stats_On_Seeded_Db_Returns_Coherent_Baseline()
    {
        var client = CreateAdminClient();

        var stats = await GetStatsAsync(client);

        stats.TotalBusinesses.Should().BeGreaterOrEqualTo(1,
            "the InMemory DB starts with the seeded Business 1");
        stats.ActiveBusinesses.Should().BeLessOrEqualTo(stats.TotalBusinesses);
        stats.InactiveBusinesses.Should().Be(stats.TotalBusinesses - stats.ActiveBusinesses,
            "the active + inactive split must always reconcile against the total");
        (stats.OnboardingCompleted + stats.OnboardingPending).Should().Be(stats.TotalBusinesses,
            "every business falls into exactly one onboarding bucket");
        stats.CreatedByMonth.Should().HaveCount(6,
            "the backfill always emits six calendar buckets ending at the current month");
        stats.ByPlan.Should().NotBeEmpty();
        stats.ByMacro.Should().NotBeEmpty();
    }

    #endregion

    #region Test 3 — ByPlan / ByMacro reflect the distribution of freshly-created businesses

    [Fact]
    public async Task IT_STATS_3_Stats_Reflects_NewlyCreated_Plan_And_Macro_Distribution()
    {
        var client = CreateAdminClient();

        var before = await GetStatsAsync(client);

        // Create 4 businesses across two plans and two macros so the
        // distribution buckets each move predictably.
        var creates = new[]
        {
            new { plan = PlanTypeIds.Basic, macro = MacroCategoryIds.Services },
            new { plan = PlanTypeIds.Basic, macro = MacroCategoryIds.Services },
            new { plan = PlanTypeIds.Pro,   macro = MacroCategoryIds.Retail },
            new { plan = PlanTypeIds.Pro,   macro = MacroCategoryIds.FoodBeverage },
        };
        foreach (var spec in creates)
        {
            var suffix = NewUniqueSuffix();
            var request = new
            {
                businessName = $"StatsTest-{suffix}",
                ownerName = $"Owner {suffix}",
                email = $"stats-{suffix}@example.com",
                password = "StatsPass123!",
                primaryMacroCategoryId = spec.macro,
                planTypeId = spec.plan
            };
            var response = await client.PostAsJsonAsync("/api/Admin/businesses", request);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var after = await GetStatsAsync(client);

        after.TotalBusinesses.Should().Be(before.TotalBusinesses + 4);

        int DeltaByPlan(AdminBusinessStatsResponse stats, int planId) =>
            stats.ByPlan.FirstOrDefault(p => p.PlanTypeId == planId)?.Count ?? 0;

        int DeltaByMacro(AdminBusinessStatsResponse stats, int macroId) =>
            stats.ByMacro.FirstOrDefault(m => m.PrimaryMacroCategoryId == macroId)?.Count ?? 0;

        (DeltaByPlan(after, PlanTypeIds.Basic) - DeltaByPlan(before, PlanTypeIds.Basic))
            .Should().Be(2, "two new Basic-plan businesses");
        (DeltaByPlan(after, PlanTypeIds.Pro) - DeltaByPlan(before, PlanTypeIds.Pro))
            .Should().Be(2, "two new Pro-plan businesses");

        (DeltaByMacro(after, MacroCategoryIds.Services) - DeltaByMacro(before, MacroCategoryIds.Services))
            .Should().Be(2, "two new Services-macro businesses");
        (DeltaByMacro(after, MacroCategoryIds.Retail) - DeltaByMacro(before, MacroCategoryIds.Retail))
            .Should().Be(1);
        (DeltaByMacro(after, MacroCategoryIds.FoodBeverage) - DeltaByMacro(before, MacroCategoryIds.FoodBeverage))
            .Should().Be(1);

        // Codes resolved by the controller — sanity check that the FE
        // does not have to maintain its own id → string map.
        after.ByPlan
            .First(p => p.PlanTypeId == PlanTypeIds.Basic).PlanTypeCode
            .Should().Be("Basic");
        after.ByMacro
            .First(m => m.PrimaryMacroCategoryId == MacroCategoryIds.Services).PrimaryMacroCategoryCode
            .Should().Be("services");
    }

    #endregion

    #region Test 4 — Trial windows respect the (NOW, NOW+N] half-open intervals

    [Fact]
    public async Task IT_STATS_4_TrialsExpiring_Counts_Use_HalfOpen_Window()
    {
        var client = CreateAdminClient();

        var before = await GetStatsAsync(client);

        // Seed three businesses straight via the DbContext so the
        // arbitrary TrialEndsAt is honored. The admin endpoint always
        // computes its own trial end (+14d for paid plans, null for Free),
        // which would not let us probe the boundary precisely.
        var nowUtc = DateTime.UtcNow;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Businesses.AddRange(
                BuildBusiness("Stats-Trial-3d", nowUtc.AddDays(3)),
                BuildBusiness("Stats-Trial-10d", nowUtc.AddDays(10)),
                BuildBusiness("Stats-Trial-20d", nowUtc.AddDays(20)));
            await db.SaveChangesAsync();
        }

        var after = await GetStatsAsync(client);

        (after.TrialsExpiring7Days - before.TrialsExpiring7Days)
            .Should().Be(1, "only the +3d trial sits inside (now, now + 7d]");
        (after.TrialsExpiring14Days - before.TrialsExpiring14Days)
            .Should().Be(2, "the +3d and +10d trials sit inside (now, now + 14d]; the +20d is outside");
    }

    #endregion

    #region Test 5 — CreatedByMonth returns six chronological buckets and reflects seeded creates

    [Fact]
    public async Task IT_STATS_5_CreatedByMonth_Returns_Six_Buckets_With_Backfill()
    {
        var client = CreateAdminClient();

        var nowUtc = DateTime.UtcNow;
        var currentMonthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var twoMonthsAgo = currentMonthStart.AddMonths(-2).AddDays(5);

        // Seed one business in the bucket two months back.
        int seededInTwoMonthsAgoBucket;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var existing = await db.Businesses
                .CountAsync(b => b.CreatedAt.Year == twoMonthsAgo.Year
                                  && b.CreatedAt.Month == twoMonthsAgo.Month);
            seededInTwoMonthsAgoBucket = existing;

            var biz = BuildBusiness("Stats-Bucket-2mo", trialEndsAt: null);
            biz.CreatedAt = twoMonthsAgo;
            db.Businesses.Add(biz);
            await db.SaveChangesAsync();
        }

        var stats = await GetStatsAsync(client);

        stats.CreatedByMonth.Should().HaveCount(6,
            "the controller backfills the calendar window even if some months are empty");

        var ordered = stats.CreatedByMonth.ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            var prev = new DateTime(ordered[i - 1].Year, ordered[i - 1].Month, 1);
            var curr = new DateTime(ordered[i].Year, ordered[i].Month, 1);
            curr.Should().Be(prev.AddMonths(1),
                "buckets must be contiguous and ordered oldest → newest");
        }

        ordered[^1].Year.Should().Be(nowUtc.Year);
        ordered[^1].Month.Should().Be(nowUtc.Month);

        var twoMonthsAgoBucket = ordered
            .First(b => b.Year == twoMonthsAgo.Year && b.Month == twoMonthsAgo.Month);
        twoMonthsAgoBucket.Count.Should().Be(seededInTwoMonthsAgoBucket + 1,
            "the bucket containing the seeded business must reflect the increment exactly");
    }

    #endregion

    #region Helpers

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminTokenHeader, TestConstants.AdminApiToken);
        return client;
    }

    private static async Task<AdminBusinessStatsResponse> GetStatsAsync(HttpClient client)
    {
        var response = await client.GetAsync(StatsRoute);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        var stats = JsonSerializer.Deserialize<AdminBusinessStatsResponse>(
            body, JsonOpts);
        stats.Should().NotBeNull();
        return stats!;
    }

    /// <summary>
    /// Builds a minimally-valid Business row for direct-to-DbContext seed.
    /// Mirrors the defaults of the Business CLR initializer so the row is
    /// indistinguishable from one inserted via the public Register path
    /// except for the precise <c>TrialEndsAt</c> / <c>CreatedAt</c> the
    /// test wants to probe.
    /// </summary>
    private static Domain.Models.Business BuildBusiness(string name, DateTime? trialEndsAt)
    {
        return new Domain.Models.Business
        {
            Name = $"{name}-{Guid.NewGuid():N}".Substring(0, 30),
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = PlanTypeIds.Basic,
            CountryCode = "MX",
            DefaultTaxId = 0, // schema enforces, but for stats counts it does not matter
            TrialEndsAt = trialEndsAt,
            TrialUsed = false,
            OnboardingStatusId = 1,
            CurrentOnboardingStep = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string NewUniqueSuffix() => Guid.NewGuid().ToString("N")[..12];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    #endregion
}
