using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.DTOs.Admin;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;
using POS.Services.IService;

namespace POS.IntegrationTests.Billing;

/// <summary>
/// PR-6 financial metrics. Each test uses its OWN factory (isolated InMemory DB) because MRR /
/// churn / retention are global aggregates that would otherwise be polluted by sibling tests.
/// All aggregation is LINQ (InMemory-translatable) — no raw SQL.
/// </summary>
public class BillingMetricsServiceTests
{
    [Fact]
    public async Task Mrr_ZeroSubscriptions_IsZero()
    {
        using var f = new CustomWebApplicationFactory();
        var m = await MetricsAsync(f);
        m.CurrentMrr.AmountCents.Should().Be(0);
        m.CurrentArr.AmountCents.Should().Be(0);
    }

    [Fact]
    public async Task Mrr_OneActiveMonthly_EqualsBase()
    {
        using var f = new CustomWebApplicationFactory();
        await SeedSubAsync(f, status: "active", baseAmount: 34900, cycle: "Monthly");

        var m = await MetricsAsync(f);
        m.CurrentMrr.AmountCents.Should().Be(34900);
        m.CurrentArr.AmountCents.Should().Be(34900 * 12);
        m.ActiveSubscriptions.Should().Be(1);
    }

    [Fact]
    public async Task Mrr_AnnualSubscription_IsNormalizedToMonthly()
    {
        using var f = new CustomWebApplicationFactory();
        await SeedSubAsync(f, status: "active", baseAmount: 358800, cycle: "Annual"); // 358800 / 12 = 29900

        (await MetricsAsync(f)).CurrentMrr.AmountCents.Should().Be(29900);
    }

    [Fact]
    public async Task Mrr_IncludesActiveAddOns()
    {
        using var f = new CustomWebApplicationFactory();
        var subId = await SeedSubAsync(f, status: "active", baseAmount: 34900, cycle: "Monthly");
        await AddActiveAddOnAsync(f, subId, customPriceCents: 5000, quantity: 1);

        (await MetricsAsync(f)).CurrentMrr.AmountCents.Should().Be(39900);
    }

    [Fact]
    public async Task Counts_SeparateActiveTrialPastDue()
    {
        using var f = new CustomWebApplicationFactory();
        await SeedSubAsync(f, status: "active", baseAmount: 1000, cycle: "Monthly");
        await SeedSubAsync(f, status: "trialing", baseAmount: 1000, cycle: "Monthly");
        await SeedSubAsync(f, status: "past_due", baseAmount: 1000, cycle: "Monthly");

        var m = await MetricsAsync(f);
        m.ActiveSubscriptions.Should().Be(1);
        m.TrialSubscriptions.Should().Be(1);
        m.PastDueSubscriptions.Should().Be(1);
        m.CurrentMrr.AmountCents.Should().Be(3000, "active + trialing + past_due all count toward MRR");
    }

    [Fact]
    public async Task Churn_CountsOnlyPaidCancels()
    {
        using var f = new CustomWebApplicationFactory();
        var bizCreated = DateTime.UtcNow.AddDays(-60);
        await SeedSubAsync(f, "active", 1000, "Monthly", bizCreated: bizCreated, withPayment: true);                       // denom
        await SeedSubAsync(f, "canceled", 1000, "Monthly", bizCreated: bizCreated, canceledAt: DateTime.UtcNow.AddDays(-10), withPayment: true);  // denom + numer
        await SeedSubAsync(f, "canceled", 1000, "Monthly", bizCreated: bizCreated, canceledAt: DateTime.UtcNow.AddDays(-10), withPayment: false); // trial-cancel: excluded

        (await MetricsAsync(f)).ChurnRate30d.Should().BeApproximately(0.5, 0.001, "1 paid cancel / 2 paid active-at-start");
    }

    [Fact]
    public async Task Retention_ReconstructsCurveFromCanceledAt()
    {
        using var f = new CustomWebApplicationFactory();
        var firstMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var cohortStart = firstMonth.AddMonths(-3);

        await SeedSubAsync(f, "canceled", 1000, "Monthly", bizCreated: cohortStart.AddDays(2), canceledAt: cohortStart.AddDays(15));            // churns in month 0
        await SeedSubAsync(f, "canceled", 1000, "Monthly", bizCreated: cohortStart.AddDays(3), canceledAt: cohortStart.AddMonths(1).AddDays(15)); // churns in month 1
        await SeedSubAsync(f, "active", 1000, "Monthly", bizCreated: cohortStart.AddDays(4));                                                     // retained

        var m = await MetricsAsync(f);
        var cohort = m.RetentionByCohort.Single(c => c.CohortMonth == cohortStart.ToString("yyyy-MM"));
        cohort.CohortSize.Should().Be(3);
        cohort.Periods[0].Rate.Should().BeApproximately(1.0, 0.01);
        cohort.Periods[1].Rate.Should().BeApproximately(2.0 / 3, 0.01);
        cohort.Periods[2].Rate.Should().BeApproximately(1.0 / 3, 0.01);
    }

    [Fact]
    public async Task Revenue_SumsCollectedPaymentsByMonth()
    {
        using var f = new CustomWebApplicationFactory();
        var subId = await SeedSubAsync(f, "active", 1000, "Monthly");
        await AddPaymentAsync(f, subId, amountCents: 12345, paidAt: DateTime.UtcNow);

        var m = await MetricsAsync(f);
        var thisMonth = DateTime.UtcNow.ToString("yyyy-MM");
        m.RevenueByMonth.Single(r => r.Month == thisMonth).AmountCents.Should().Be(12345);
        m.RevenueByMonth.Should().HaveCount(12, "default lookback fills 12 months");
    }

    #region Helpers

    private static async Task<AdminBillingMetricsDto> MetricsAsync(CustomWebApplicationFactory f)
    {
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBillingMetricsService>();
        return await svc.GetMetricsAsync();
    }

    private static async Task<int> SeedSubAsync(
        CustomWebApplicationFactory f, string status, int baseAmount, string cycle,
        DateTime? canceledAt = null, DateTime? bizCreated = null, bool withPayment = false)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var railId = await db.SaaSBillingMethods.Where(x => x.Code == "BankTransfer").Select(x => x.Id).FirstAsync();

        var biz = new Business { Name = $"Biz-{Guid.NewGuid():N}", CreatedAt = bizCreated ?? DateTime.UtcNow, IsActive = true };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();

        var sub = new Subscription
        {
            BusinessId = biz.Id,
            StripeCustomerId = $"cus_{biz.Id}",
            StripeSubscriptionId = $"sub_{biz.Id}",
            PlanTypeId = PlanTypeIds.Basic,
            BillingCycle = cycle,
            PricingGroup = "General",
            Status = status,
            CurrentPeriodStart = DateTime.UtcNow,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(1),
            UpdatedAt = DateTime.UtcNow,
            BillingMethodId = railId,
            BaseAmountCents = baseAmount,
            Currency = "MXN",
            CanceledAt = canceledAt
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        if (withPayment) await AddPaymentInternalAsync(db, sub.Id, railId, 1000, DateTime.UtcNow.AddDays(-40));
        return sub.Id;
    }

    private static async Task AddActiveAddOnAsync(CustomWebApplicationFactory f, int subId, int customPriceCents, int quantity)
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

    private static async Task AddPaymentAsync(CustomWebApplicationFactory f, int subId, int amountCents, DateTime paidAt)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var railId = await db.SaaSBillingMethods.Where(x => x.Code == "BankTransfer").Select(x => x.Id).FirstAsync();
        await AddPaymentInternalAsync(db, subId, railId, amountCents, paidAt);
    }

    private static async Task AddPaymentInternalAsync(ApplicationDbContext db, int subId, int railId, int amountCents, DateTime paidAt)
    {
        var businessId = await db.Subscriptions.IgnoreQueryFilters().Where(s => s.Id == subId).Select(s => s.BusinessId).FirstAsync();
        var invoice = new SubscriptionInvoice
        {
            SubscriptionId = subId,
            BusinessId = businessId,
            InvoiceNumber = 1,
            Status = Domain.Enums.SubscriptionInvoiceStatus.Paid,
            IssuedAtUtc = paidAt,
            DueDate = paidAt.AddDays(7),
            PeriodStart = paidAt,
            PeriodEnd = paidAt.AddMonths(1),
            SubtotalCents = amountCents,
            TaxCents = 0,
            TotalCents = amountCents,
            Currency = "MXN"
        };
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();

        db.TenantPayments.Add(new TenantPayment
        {
            InvoiceId = invoice.Id,
            BillingMethodId = railId,
            AmountCents = amountCents,
            Currency = "MXN",
            PaidAtUtc = paidAt
        });
        await db.SaveChangesAsync();
    }

    #endregion
}
