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
/// PR-4 add-on lifecycle on a MANUAL rail (BankTransfer) — no Stripe SDK call. Activate inserts
/// a SubscriptionAddOn + BusinessAuditLog(AddOnActivated); deactivate soft-sets DeactivatedAt;
/// re-activation inserts a fresh row (history preserved); a duplicate active add-on is rejected
/// (409). The Stripe-rail activation path uses the live SDK and is runtime-validated at the end
/// of the roadmap.
/// </summary>
public class SubscriptionAddOnLifecycleTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminBusinesses = "/api/Admin/businesses";

    private readonly CustomWebApplicationFactory _factory;

    public SubscriptionAddOnLifecycleTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Activate_PersistsRowAndWritesAudit()
    {
        var (businessId, subId) = await SeedManualSubscriptionAsync();
        var addOnId = await AddOnIdAsync("device_kds");

        var resp = await Admin().PostAsJsonAsync($"{AdminBusinesses}/{businessId}/subscription/add-ons",
            new { addOnId, quantity = 2, reason = "Cliente pidió KDS extra" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var row = await db.SubscriptionAddOns.FirstAsync(a => a.SubscriptionId == subId);
        row.AddOnId.Should().Be(addOnId);
        row.Quantity.Should().Be(2);
        row.DeactivatedAt.Should().BeNull();
        row.StripeItemId.Should().BeNull("manual rail never touches Stripe");

        (await db.Set<BusinessAuditLog>()
            .AnyAsync(a => a.BusinessId == businessId && a.Action == BusinessAuditAction.AddOnActivated))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Deactivate_SoftSetsDeactivatedAt()
    {
        var (businessId, subId) = await SeedManualSubscriptionAsync();
        var addOnId = await AddOnIdAsync("device_kds");
        await Admin().PostAsJsonAsync($"{AdminBusinesses}/{businessId}/subscription/add-ons",
            new { addOnId, quantity = 1 });

        var subAddOnId = await ActiveAddOnIdAsync(subId);
        var del = await Admin().DeleteAsync($"{AdminBusinesses}/{businessId}/subscription/add-ons/{subAddOnId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.SubscriptionAddOns.FirstAsync(a => a.Id == subAddOnId)).DeactivatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Reactivate_InsertsNewRow_KeepingHistory()
    {
        var (businessId, subId) = await SeedManualSubscriptionAsync();
        var addOnId = await AddOnIdAsync("device_kds");

        await Admin().PostAsJsonAsync($"{AdminBusinesses}/{businessId}/subscription/add-ons", new { addOnId, quantity = 1 });
        var first = await ActiveAddOnIdAsync(subId);
        await Admin().DeleteAsync($"{AdminBusinesses}/{businessId}/subscription/add-ons/{first}");

        var reactivate = await Admin().PostAsJsonAsync($"{AdminBusinesses}/{businessId}/subscription/add-ons",
            new { addOnId, quantity = 1 });
        reactivate.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var all = await db.SubscriptionAddOns.Where(a => a.SubscriptionId == subId).ToListAsync();
        all.Should().HaveCount(2, "re-activation inserts a fresh row, history is kept");
        all.Count(a => a.DeactivatedAt == null).Should().Be(1, "exactly one active instance");
    }

    [Fact]
    public async Task Activate_DuplicateActive_Returns409()
    {
        var (businessId, _) = await SeedManualSubscriptionAsync();
        var addOnId = await AddOnIdAsync("device_kds");

        await Admin().PostAsJsonAsync($"{AdminBusinesses}/{businessId}/subscription/add-ons", new { addOnId, quantity = 1 });
        var dup = await Admin().PostAsJsonAsync($"{AdminBusinesses}/{businessId}/subscription/add-ons",
            new { addOnId, quantity = 1 });

        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    #region Helpers

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        return c;
    }

    private async Task<int> AddOnIdAsync(string code)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.PlanAddOns.Where(a => a.Code == code).Select(a => a.Id).FirstAsync();
    }

    private async Task<int> ActiveAddOnIdAsync(int subId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.SubscriptionAddOns
            .Where(a => a.SubscriptionId == subId && a.DeactivatedAt == null)
            .Select(a => a.Id).FirstAsync();
    }

    private async Task<(int BusinessId, int SubId)> SeedManualSubscriptionAsync()
    {
        var businessId = await CreateBusinessAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var railId = await db.SaaSBillingMethods.Where(m => m.Code == "BankTransfer").Select(m => m.Id).FirstAsync();

        var sub = new Subscription
        {
            BusinessId = businessId,
            StripeCustomerId = $"cus_{businessId}",
            StripeSubscriptionId = $"sub_{businessId}",
            PlanTypeId = PlanTypeIds.Basic,
            BillingCycle = "Monthly",
            PricingGroup = "General",
            Status = "active",
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            UpdatedAt = DateTime.UtcNow,
            BillingMethodId = railId,
            BaseAmountCents = 14900,
            Currency = "MXN"
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();
        return (businessId, sub.Id);
    }

    private async Task<int> CreateBusinessAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var resp = await Admin().PostAsJsonAsync(AdminBusinesses, new
        {
            businessName = $"AddOn-{suffix}",
            ownerName = $"Owner {suffix}",
            email = $"addon-{suffix}@example.com",
            password = "AddOnPass123!",
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
