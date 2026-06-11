using System.Net.Http.Json;
using POS.Domain.DTOs.Admin;
using POS.IntegrationTests.Infrastructure;

namespace POS.IntegrationTests.Admin;

/// <summary>
/// PR-UI-prep GAP-C: read-only SaaS-billing catalog readers that back UI selectors —
/// <c>GET /api/Admin/billing-methods</c> (rails) and <c>GET /api/Admin/plan-add-ons</c>.
/// Both serve code-seeded system data, so a shared factory is sufficient.
/// </summary>
public class BillingCatalogReaderTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public BillingCatalogReaderTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task BillingMethods_ReturnsSevenRails_OrderedBySortOrder()
    {
        var rails = await Admin().GetFromJsonAsync<List<SaaSBillingMethodDto>>("/api/Admin/billing-methods");

        rails.Should().HaveCount(7);
        rails!.Select(r => r.SortOrder).Should().BeInAscendingOrder();
        rails[0].Code.Should().Be("Stripe"); // SortOrder 1
        rails.Should().Contain(r => r.Code == "BankTransfer" && r.RequiresReference && !r.IsAutomatic);
        rails.Should().OnlyContain(r => r.IsSystem);
    }

    [Fact]
    public async Task PlanAddOns_ReturnsFullCatalog()
    {
        var addOns = await Admin().GetFromJsonAsync<List<PlanAddOnDto>>("/api/Admin/plan-add-ons");

        addOns.Should().HaveCountGreaterThanOrEqualTo(3);
        addOns.Should().Contain(a => a.Code == "device_kds");
        addOns.Should().Contain(a => a.Code == "device_kiosk");
        addOns.Should().Contain(a => a.Code == "device_cashier");
        addOns!.Where(a => a.Code.StartsWith("device_"))
            .Should().OnlyContain(a => a.DefaultPriceCents >= 0 && a.Currency == "MXN");
    }

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        return c;
    }
}
