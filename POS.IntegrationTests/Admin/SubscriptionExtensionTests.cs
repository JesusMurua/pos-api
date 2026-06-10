using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Admin;

/// <summary>
/// PR-1b foundation: the new Subscription columns default correctly (Currency,
/// CfdiRequired) and the nullable-defer columns (BillingMethodId, BaseAmountCents)
/// start null until PR-2; the SubscriptionPriceHistory table + FK work. The raw-SQL
/// backfill itself runs only under Npgsql migrations (not the InMemory test path),
/// so it is validated by review + prod recon, not here.
/// </summary>
public class SubscriptionExtensionTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminRoute = "/api/Admin/businesses";

    private readonly CustomWebApplicationFactory _factory;

    public SubscriptionExtensionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task NewColumns_DefaultCorrectly_AndBillingMethodPersists()
    {
        var businessId = await CreateBusinessAsync();

        int subId;
        int railId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            railId = await db.SaaSBillingMethods.Where(m => m.Code == "Stripe").Select(m => m.Id).FirstAsync();
            var sub = NewSubscription(businessId, railId);
            db.Subscriptions.Add(sub);
            await db.SaveChangesAsync();
            subId = sub.Id;
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.Id == subId);

            sub.Currency.Should().Be("MXN", "default applies without a creator setting it");
            sub.CfdiRequired.Should().BeFalse();
            sub.BillingMethodId.Should().Be(railId, "PR-2: BillingMethodId is required (the rail)");
            sub.BaseAmountCents.Should().Be(14900);
        }
    }

    [Fact]
    public async Task SubscriptionPriceHistory_InsertsAndReadsBack()
    {
        var businessId = await CreateBusinessAsync();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var railId = await db.SaaSBillingMethods.Where(m => m.Code == "Stripe").Select(m => m.Id).FirstAsync();
        var sub = NewSubscription(businessId, railId);
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        db.Set<SubscriptionPriceHistory>().Add(new SubscriptionPriceHistory
        {
            SubscriptionId = sub.Id,
            BeforeAmountCents = null,
            AfterAmountCents = 14900,
            ChangedAtUtc = DateTime.UtcNow,
            ChangedByTokenId = "ops-test",
            Reason = "Negotiated launch price",
            EffectiveDate = DateTime.UtcNow,
            AppliedToInvoiceId = null
        });
        await db.SaveChangesAsync();

        var row = await db.Set<SubscriptionPriceHistory>().FirstAsync(h => h.SubscriptionId == sub.Id);
        row.AfterAmountCents.Should().Be(14900);
        row.Reason.Should().Be("Negotiated launch price");
        row.AppliedToInvoiceId.Should().BeNull("the FK to SubscriptionInvoice arrives in PR-3");
    }

    #region Helpers

    private static Subscription NewSubscription(int businessId, int billingMethodId) => new()
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
        BillingMethodId = billingMethodId,
        BaseAmountCents = 14900
    };

    private async Task<int> CreateBusinessAsync()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var resp = await client.PostAsJsonAsync(AdminRoute, new
        {
            businessName = $"Sub-{suffix}",
            ownerName = $"Owner {suffix}",
            email = $"sub-{suffix}@example.com",
            password = "SubPass123!",
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
