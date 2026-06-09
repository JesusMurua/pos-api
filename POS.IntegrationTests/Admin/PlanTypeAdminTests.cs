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
/// Admin (X-Admin-Token) editing of the plan catalog price (OQ-3). The edit must
/// persist, survive the boot reseed (upsert-except-MonthlyPrice), reject bad input,
/// and reflect on the public catalog. Code is immutable (not in the payload).
/// </summary>
public class PlanTypeAdminTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminRoute = "/api/Admin/plan-types";

    private readonly CustomWebApplicationFactory _factory;

    public PlanTypeAdminTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Update_EditsMonthlyPrice_Persisted()
    {
        var client = Admin();

        var put = await client.PutAsJsonAsync($"{AdminRoute}/{PlanTypeIds.Basic}",
            new AdminUpdatePlanTypeRequest { Name = "Básico", SortOrder = 1, Currency = "MXN", MonthlyPrice = 199m });
        put.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await PriceAsync(PlanTypeIds.Basic)).Should().Be(199m);
    }

    [Fact]
    public async Task Update_PriceSurvivesReseed()
    {
        var client = Admin();

        await client.PutAsJsonAsync($"{AdminRoute}/{PlanTypeIds.Pro}",
            new AdminUpdatePlanTypeRequest { Name = "Pro", SortOrder = 2, Currency = "MXN", MonthlyPrice = 999m });

        // Re-run the boot reseed: it must NOT revert the admin-set price.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await DbInitializer.SeedSystemDataAsync(db);
        }

        (await PriceAsync(PlanTypeIds.Pro)).Should().Be(999m, "reseed is upsert-except-MonthlyPrice");
    }

    [Fact]
    public async Task Update_UnknownId_404()
    {
        var resp = await Admin().PutAsJsonAsync($"{AdminRoute}/99999",
            new AdminUpdatePlanTypeRequest { Name = "X", SortOrder = 0, Currency = "MXN", MonthlyPrice = 10m });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_NegativePrice_400()
    {
        var resp = await Admin().PutAsJsonAsync($"{AdminRoute}/{PlanTypeIds.Basic}",
            new AdminUpdatePlanTypeRequest { Name = "Básico", SortOrder = 1, Currency = "MXN", MonthlyPrice = -5m });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Update_InvalidatesPublicCatalog()
    {
        var client = Admin();

        // Warm the public cache with the seeded name first.
        (await _factory.CreateClient().GetAsync("/api/Catalog/plan-types"))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await client.PutAsJsonAsync($"{AdminRoute}/{PlanTypeIds.Free}",
            new AdminUpdatePlanTypeRequest { Name = "GratisEdit", SortOrder = 0, Currency = "MXN", MonthlyPrice = 0m });

        // The edit invalidated PlanTypes — the refetch must NOT be served stale.
        var publicResp = await _factory.CreateClient().GetAsync("/api/Catalog/plan-types");
        publicResp.StatusCode.Should().Be(HttpStatusCode.OK);
        (await publicResp.Content.ReadAsStringAsync())
            .Should().Contain("GratisEdit", "the PlanTypes cache was invalidated, not served stale");
    }

    #region Helpers

    private HttpClient Admin()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        return client;
    }

    private async Task<decimal?> PriceAsync(int planTypeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.PlanTypeCatalogs.Where(p => p.Id == planTypeId)
            .Select(p => p.MonthlyPrice).FirstAsync();
    }

    #endregion
}
