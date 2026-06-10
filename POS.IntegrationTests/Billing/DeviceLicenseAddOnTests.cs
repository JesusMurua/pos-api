using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Billing;

/// <summary>
/// PR-4 device-licensing rewire: the primitive that DeviceService.GetUsageAndLimitAsync now
/// reads (ISubscriptionAddOnRepository.SumActiveQuantityByLinkAsync) replaces the retired
/// SubscriptionItem sum. Locks the CRITICAL fail-strict contract: only DeactivatedAt-null
/// add-ons on active/trialing subscriptions count toward a tenant's device capacity. The full
/// DeviceService enforcement integration is runtime-validated at the end of the roadmap.
/// </summary>
public class DeviceLicenseAddOnTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public DeviceLicenseAddOnTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task ActiveAddOn_CountsTowardFeatureCapacity()
    {
        var businessId = await CreateBusinessAsync();
        await SeedAddOnAsync(businessId, "device_kds", quantity: 2, status: "active", deactivated: false);

        (await SumKdsAsync(businessId)).Should().Be(2);
    }

    [Fact]
    public async Task DeactivatedAddOn_IsExcluded()
    {
        var businessId = await CreateBusinessAsync();
        await SeedAddOnAsync(businessId, "device_kds", quantity: 3, status: "active", deactivated: true);

        (await SumKdsAsync(businessId)).Should().Be(0, "deactivated add-ons never grant capacity");
    }

    [Fact]
    public async Task PastDueSubscription_DoesNotContribute()
    {
        var businessId = await CreateBusinessAsync();
        await SeedAddOnAsync(businessId, "device_kds", quantity: 5, status: "past_due", deactivated: false);

        (await SumKdsAsync(businessId)).Should().Be(0, "fail-strict: only active/trialing subscriptions count");
    }

    [Fact]
    public async Task WrongFeature_IsNotCounted()
    {
        var businessId = await CreateBusinessAsync();
        await SeedAddOnAsync(businessId, "device_kiosk", quantity: 4, status: "active", deactivated: false);

        (await SumKdsAsync(businessId)).Should().Be(0, "a kiosk add-on does not grant KDS capacity");
    }

    #region Helpers

    private async Task<int> SumKdsAsync(int businessId)
    {
        using var scope = _factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        return await uow.SubscriptionAddOns.SumActiveQuantityByLinkAsync(businessId, FeatureKey.MaxKdsScreens);
    }

    private async Task SeedAddOnAsync(int businessId, string addOnCode, int quantity, string status, bool deactivated)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var railId = await db.SaaSBillingMethods.Where(m => m.Code == "BankTransfer").Select(m => m.Id).FirstAsync();
        var addOnId = await db.PlanAddOns.Where(a => a.Code == addOnCode).Select(a => a.Id).FirstAsync();

        var sub = new Subscription
        {
            BusinessId = businessId,
            StripeCustomerId = $"cus_{businessId}",
            StripeSubscriptionId = $"sub_{businessId}",
            PlanTypeId = PlanTypeIds.Basic,
            BillingCycle = "Monthly",
            PricingGroup = "General",
            Status = status,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            UpdatedAt = DateTime.UtcNow,
            BillingMethodId = railId,
            BaseAmountCents = 14900,
            Currency = "MXN"
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        db.SubscriptionAddOns.Add(new SubscriptionAddOn
        {
            SubscriptionId = sub.Id,
            AddOnId = addOnId,
            Quantity = quantity,
            ActivatedAt = DateTime.UtcNow,
            DeactivatedAt = deactivated ? DateTime.UtcNow : null
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> CreateBusinessAsync()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var resp = await client.PostAsJsonAsync("/api/Admin/businesses", new
        {
            businessName = $"Lic-{suffix}",
            ownerName = $"Owner {suffix}",
            email = $"lic-{suffix}@example.com",
            password = "LicPass123!",
            primaryMacroCategoryId = MacroCategoryIds.Services,
            planTypeId = PlanTypeIds.Basic,
            countryCode = "MX"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("businessId").GetInt32();
    }

    #endregion
}
