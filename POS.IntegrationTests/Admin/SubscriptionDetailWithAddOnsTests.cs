using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.DTOs.Admin;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Admin;

/// <summary>
/// PR-UI-prep GAP-B: the subscription detail response carries <c>activeAddOns[]</c>,
/// excludes deactivated rows, and resolves <c>EffectivePriceCents = CustomPriceCents ?? DefaultPriceCents</c>.
/// </summary>
public class SubscriptionDetailWithAddOnsTests
{
    [Fact]
    public async Task IncludesActiveAddOns_ExcludesDeactivated_ResolvesEffectivePrice()
    {
        using var f = new CustomWebApplicationFactory();
        var (bizId, subId, kdsDefault) = await SeedAsync(f);

        var detail = await Admin(f).GetFromJsonAsync<AdminSubscriptionDetailDto>(
            $"/api/Admin/businesses/{bizId}/subscription");

        detail!.ActiveAddOns.Should().HaveCount(2); // the deactivated one is excluded

        var custom = detail.ActiveAddOns.Single(a => a.AddOnCode == "device_kds");
        custom.CustomPriceCents.Should().Be(5000);
        custom.EffectivePriceCents.Should().Be(5000); // custom wins
        custom.Quantity.Should().Be(2);

        var catalogPriced = detail.ActiveAddOns.Single(a => a.AddOnCode == "device_kiosk");
        catalogPriced.CustomPriceCents.Should().BeNull();
        catalogPriced.EffectivePriceCents.Should().Be(catalogPriced.DefaultPriceCents); // falls back to default

        detail.ActiveAddOns.Should().NotContain(a => a.AddOnCode == "device_cashier"); // deactivated
        _ = kdsDefault;
    }

    #region Helpers

    private static HttpClient Admin(CustomWebApplicationFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        return c;
    }

    private static async Task<(int bizId, int subId, int kdsDefault)> SeedAsync(CustomWebApplicationFactory f)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var bankTransferId = await db.SaaSBillingMethods.Where(m => m.Code == "BankTransfer").Select(m => m.Id).FirstAsync();
        var kds = await db.PlanAddOns.Where(a => a.Code == "device_kds").Select(a => new { a.Id, a.DefaultPriceCents }).FirstAsync();
        var kiosk = await db.PlanAddOns.Where(a => a.Code == "device_kiosk").Select(a => a.Id).FirstAsync();
        var cashier = await db.PlanAddOns.Where(a => a.Code == "device_cashier").Select(a => a.Id).FirstAsync();

        var biz = new Business { Name = $"AddOns-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow, IsActive = true };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();

        var sub = new Subscription
        {
            BusinessId = biz.Id,
            StripeCustomerId = $"cus_{biz.Id}",
            StripeSubscriptionId = $"sub_{biz.Id}",
            PlanTypeId = PlanTypeIds.Basic,
            BillingCycle = "Monthly",
            PricingGroup = "General",
            Status = "active",
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            UpdatedAt = DateTime.UtcNow,
            BillingMethodId = bankTransferId,
            BaseAmountCents = 14900,
            Currency = "MXN"
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        db.SubscriptionAddOns.AddRange(
            new SubscriptionAddOn { SubscriptionId = sub.Id, AddOnId = kds.Id, Quantity = 2, ActivatedAt = DateTime.UtcNow.AddDays(-3), CustomPriceCents = 5000 },
            new SubscriptionAddOn { SubscriptionId = sub.Id, AddOnId = kiosk, Quantity = 1, ActivatedAt = DateTime.UtcNow.AddDays(-2) },
            new SubscriptionAddOn { SubscriptionId = sub.Id, AddOnId = cashier, Quantity = 1, ActivatedAt = DateTime.UtcNow.AddDays(-5), DeactivatedAt = DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();

        return (biz.Id, sub.Id, kds.DefaultPriceCents);
    }

    #endregion
}
