using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.DTOs.Admin;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Admin;

/// <summary>
/// PR-2 admin subscription reconcile on a MANUAL rail (BankTransfer): a price change
/// never calls Stripe — it persists locally + writes SubscriptionPriceHistory +
/// BusinessAuditLog(SubscriptionPriceChanged), atomically. (The Stripe-rail path uses
/// the live SDK and is runtime-validated at the end of the roadmap.)
/// </summary>
public class AdminSubscriptionReconcileTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminBusinesses = "/api/Admin/businesses";

    private readonly CustomWebApplicationFactory _factory;

    public AdminSubscriptionReconcileTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ManualRail_PriceChange_PersistsAndWritesHistoryAndAudit()
    {
        var businessId = await CreateBusinessAsync();
        var subId = await SeedManualRailSubscriptionAsync(businessId, initialAmount: 14900);

        var resp = await Admin().PutAsJsonAsync($"{AdminBusinesses}/{businessId}/subscription",
            new AdminUpdateSubscriptionRequest { BaseAmountCents = 25000, Reason = "Negociado" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.Id == subId);
        sub.BaseAmountCents.Should().Be(25000);

        var history = await db.SubscriptionPriceHistories.Where(h => h.SubscriptionId == subId).ToListAsync();
        history.Should().ContainSingle();
        history[0].BeforeAmountCents.Should().Be(14900);
        history[0].AfterAmountCents.Should().Be(25000);
        history[0].Reason.Should().Be("Negociado");

        (await db.Set<BusinessAuditLog>()
            .AnyAsync(a => a.BusinessId == businessId && a.Action == BusinessAuditAction.SubscriptionPriceChanged))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Get_ReturnsDetail_WithPriceHistory()
    {
        var businessId = await CreateBusinessAsync();
        await SeedManualRailSubscriptionAsync(businessId, initialAmount: 14900);

        await Admin().PutAsJsonAsync($"{AdminBusinesses}/{businessId}/subscription",
            new AdminUpdateSubscriptionRequest { BaseAmountCents = 30000, Reason = "Descuento removido" });

        var detail = await Admin().GetFromJsonAsync<AdminSubscriptionDetailDto>(
            $"{AdminBusinesses}/{businessId}/subscription");

        detail!.BaseAmountCents.Should().Be(30000);
        detail.BillingMethodCode.Should().Be("BankTransfer");
        detail.PriceHistory.Should().ContainSingle(h => h.AfterAmountCents == 30000);
    }

    [Fact]
    public async Task Update_NoSubscription_404()
    {
        var businessId = await CreateBusinessAsync(); // no subscription seeded
        var resp = await Admin().PutAsJsonAsync($"{AdminBusinesses}/{businessId}/subscription",
            new AdminUpdateSubscriptionRequest { BaseAmountCents = 1000 });
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #region Helpers

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        return c;
    }

    private async Task<int> CreateBusinessAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var resp = await Admin().PostAsJsonAsync(AdminBusinesses, new
        {
            businessName = $"Recon-{suffix}",
            ownerName = $"Owner {suffix}",
            email = $"recon-{suffix}@example.com",
            password = "ReconPass123!",
            primaryMacroCategoryId = MacroCategoryIds.Services,
            planTypeId = PlanTypeIds.Basic,
            countryCode = "MX"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("businessId").GetInt32();
    }

    private async Task<int> SeedManualRailSubscriptionAsync(int businessId, int initialAmount)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var bankTransferId = await db.SaaSBillingMethods
            .Where(m => m.Code == "BankTransfer").Select(m => m.Id).FirstAsync();

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
            BillingMethodId = bankTransferId, // manual rail → reconcile skips Stripe
            BaseAmountCents = initialAmount,
            Currency = "MXN"
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();
        return sub.Id;
    }

    #endregion
}
