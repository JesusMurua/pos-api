using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Catalogs;

/// <summary>
/// Locks the StripePlanPrice catalog seeded in PR-2 (DB-backed replacement of the
/// static PriceMap). Critically, asserts the latent-bug fix: the retired PriceMap
/// labelled Basic prices "Basico", which the string-parse resolver silently bucketed
/// to Free — the new seed maps them to Basic. See docs/saas-billing-architecture.md §5.
/// </summary>
public class StripePlanPriceSeedTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public StripePlanPriceSeedTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seed_Creates18Entries_UniquePriceIds()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rows = await db.StripePlanPrices.AsNoTracking().ToListAsync();

        rows.Should().HaveCount(18, "3 tiers × 3 groups × 2 cycles");
        rows.Select(r => r.StripePriceId).Distinct().Should().HaveCount(18);
        rows.Select(r => r.PlanTypeId).Distinct().Should()
            .BeEquivalentTo(new[] { PlanTypeIds.Basic, PlanTypeIds.Pro, PlanTypeIds.Enterprise },
                "Free is not sold via Stripe");
    }

    [Fact]
    public async Task Seed_BasicoPrices_MapToBasic_NotFree()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // The 6 Basic-tier price ids (formerly "Basico" in the buggy PriceMap).
        var basicGeneralMonthly = await db.StripePlanPrices.AsNoTracking()
            .FirstAsync(p => p.StripePriceId == StripeConstants.Basico.General.Monthly);

        basicGeneralMonthly.PlanTypeId.Should().Be(PlanTypeIds.Basic,
            "the latent PriceMap bug (Basico→Free) is fixed by the explicit catalog mapping");
        basicGeneralMonthly.BillingCycle.Should().Be("Monthly");
        basicGeneralMonthly.PricingGroup.Should().Be("General");

        (await db.StripePlanPrices.AsNoTracking().CountAsync(p => p.PlanTypeId == PlanTypeIds.Basic))
            .Should().Be(6);
    }
}
