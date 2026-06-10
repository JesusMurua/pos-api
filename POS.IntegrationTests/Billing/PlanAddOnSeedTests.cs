using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Billing;

/// <summary>
/// Locks the PlanAddOn catalog seeded in PR-4 (DB-backed replacement of the retired
/// AddonPriceMap). The 3 device-license dummies must materialize 1:1 with LinkType
/// DeviceLicense and LinkedEntityId = the integer value of the FeatureKey they unlock.
/// </summary>
public class PlanAddOnSeedTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PlanAddOnSeedTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Seed_CreatesThreeDeviceLicenseAddOns()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rows = await db.PlanAddOns.AsNoTracking().ToListAsync();

        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(a => a.IsSystem && a.IsActive);
        rows.Should().OnlyContain(a => a.LinkType == PlanAddOnLinkType.DeviceLicense);
        rows.Should().OnlyContain(a => a.BillingCycle == PlanAddOnBillingCycle.Monthly);
    }

    [Fact]
    public async Task Seed_MapsFeatureKeysAndStripePlaceholders()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var kds = await db.PlanAddOns.AsNoTracking().FirstAsync(a => a.Code == "device_kds");
        kds.LinkedEntityId.Should().Be((int)FeatureKey.MaxKdsScreens);
        kds.StripePriceId.Should().Be(StripeConstants.AddOnPlaceholders.Kds);

        var kiosk = await db.PlanAddOns.AsNoTracking().FirstAsync(a => a.Code == "device_kiosk");
        kiosk.LinkedEntityId.Should().Be((int)FeatureKey.MaxKiosks);

        var cashier = await db.PlanAddOns.AsNoTracking().FirstAsync(a => a.Code == "device_cashier");
        cashier.LinkedEntityId.Should().Be((int)FeatureKey.MaxCashRegisters);
    }
}
