using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;
using POS.Services.IService;

namespace POS.IntegrationTests.Billing;

/// <summary>
/// PR-6 upcoming invoices: manual-rail subscriptions due within the window, with the Stripe rail
/// excluded (the Stripe Dashboard is authoritative there). Isolated factory per test (global query).
/// </summary>
public class UpcomingInvoicesTests
{
    [Fact]
    public async Task ReturnsManualRailDueWithinWindow()
    {
        using var f = new CustomWebApplicationFactory();
        await SeedSubAsync(f, "BankTransfer", nextBillingInDays: 5, baseAmount: 14900, status: "active");

        var list = await UpcomingAsync(f, days: 30);
        list.Should().ContainSingle();
        list[0].EstimatedAmountCents.Should().Be(14900);
        list[0].Rail.Should().Be("BankTransfer");
    }

    [Fact]
    public async Task ExcludesStripeRail()
    {
        using var f = new CustomWebApplicationFactory();
        await SeedSubAsync(f, "Stripe", nextBillingInDays: 5, baseAmount: 14900, status: "active");

        (await UpcomingAsync(f, days: 30)).Should().BeEmpty("Stripe-rail upcoming is owned by Stripe");
    }

    [Fact]
    public async Task ExcludesBeyondWindow()
    {
        using var f = new CustomWebApplicationFactory();
        await SeedSubAsync(f, "BankTransfer", nextBillingInDays: 60, baseAmount: 14900, status: "active");

        (await UpcomingAsync(f, days: 30)).Should().BeEmpty();
    }

    [Fact]
    public async Task EstimateIncludesAddOns()
    {
        using var f = new CustomWebApplicationFactory();
        var subId = await SeedSubAsync(f, "BankTransfer", nextBillingInDays: 5, baseAmount: 14900, status: "active");
        await AddAddOnAsync(f, subId, 3000, 2); // 6000

        var list = await UpcomingAsync(f, days: 30);
        list.Single().EstimatedAmountCents.Should().Be(14900 + 6000);
    }

    #region Helpers

    private static async Task<IReadOnlyList<Domain.DTOs.Admin.UpcomingInvoiceDto>> UpcomingAsync(
        CustomWebApplicationFactory f, int days)
    {
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBillingMetricsService>();
        return await svc.GetUpcomingInvoicesAsync(days);
    }

    private static async Task<int> SeedSubAsync(
        CustomWebApplicationFactory f, string railCode, int nextBillingInDays, int baseAmount, string status)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var railId = await db.SaaSBillingMethods.Where(x => x.Code == railCode).Select(x => x.Id).FirstAsync();

        var biz = new Business { Name = $"Biz-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow, IsActive = true };
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
            Status = status,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            UpdatedAt = DateTime.UtcNow,
            BillingMethodId = railId,
            BaseAmountCents = baseAmount,
            Currency = "MXN",
            NextBillingDate = DateTime.UtcNow.AddDays(nextBillingInDays)
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();
        return sub.Id;
    }

    private static async Task AddAddOnAsync(CustomWebApplicationFactory f, int subId, int customPriceCents, int quantity)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var addOnId = await db.PlanAddOns.Where(a => a.Code == "device_kds").Select(a => a.Id).FirstAsync();
        db.SubscriptionAddOns.Add(new SubscriptionAddOn
        {
            SubscriptionId = subId,
            AddOnId = addOnId,
            Quantity = quantity,
            ActivatedAt = DateTime.UtcNow,
            CustomPriceCents = customPriceCents
        });
        await db.SaveChangesAsync();
    }

    #endregion
}
