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
using POS.Services.IService;

namespace POS.IntegrationTests.Billing;

/// <summary>
/// PR-3 invoice-generation job (manual rails only). Covers the paths that do NOT hit the
/// raw-SQL per-business counter (review-only on InMemory): Stripe-rail subscriptions are
/// skipped, an already-generated period is idempotent (and still advances NextBillingDate),
/// a price-less subscription is skipped, and the overdue sweep transitions the right rows.
/// The happy-path invoice creation (counter) is validated by review + prod, like the worker.
/// </summary>
public class InvoiceGenerationServiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public InvoiceGenerationServiceTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task StripeRailSubscription_IsNotInvoiced()
    {
        var businessId = await CreateBusinessAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stripeRail = await db.SaaSBillingMethods.Where(m => m.Code == "Stripe").Select(m => m.Id).FirstAsync();
            db.Subscriptions.Add(NewDueSubscription(businessId, stripeRail, baseAmount: 14900));
            await db.SaveChangesAsync();
        }

        var generated = await RunGenerationAsync();

        generated.Should().Be(0);
        (await InvoiceCount(businessId)).Should().Be(0, "Stripe auto-generates its own invoices");
    }

    [Fact]
    public async Task PriceLessSubscription_IsSkipped()
    {
        var businessId = await CreateBusinessAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rail = await db.SaaSBillingMethods.Where(m => m.Code == "BankTransfer").Select(m => m.Id).FirstAsync();
            db.Subscriptions.Add(NewDueSubscription(businessId, rail, baseAmount: null));
            await db.SaveChangesAsync();
        }

        await RunGenerationAsync();

        (await InvoiceCount(businessId)).Should().Be(0);
    }

    [Fact]
    public async Task AlreadyGeneratedPeriod_IsIdempotent_AndAdvancesNextBillingDate()
    {
        var businessId = await CreateBusinessAsync();
        var dueDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        int subId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rail = await db.SaaSBillingMethods.Where(m => m.Code == "BankTransfer").Select(m => m.Id).FirstAsync();
            var sub = NewDueSubscription(businessId, rail, baseAmount: 14900);
            sub.NextBillingDate = dueDate;
            db.Subscriptions.Add(sub);
            await db.SaveChangesAsync();
            subId = sub.Id;

            // A manual invoice already exists for this exact period (StripeInvoiceId null).
            db.SubscriptionInvoices.Add(new SubscriptionInvoice
            {
                SubscriptionId = subId,
                BusinessId = businessId,
                InvoiceNumber = 1,
                Status = SubscriptionInvoiceStatus.Open,
                IssuedAtUtc = dueDate,
                DueDate = dueDate.AddDays(7),
                PeriodStart = dueDate,
                PeriodEnd = dueDate.AddMonths(1),
                SubtotalCents = 14900,
                TaxCents = 2384,
                TotalCents = 17284,
                Currency = "MXN"
            });
            await db.SaveChangesAsync();
        }

        var generated = await RunGenerationAsync();

        generated.Should().Be(0, "the period was already invoiced");
        (await InvoiceCount(businessId)).Should().Be(1, "no duplicate created");

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.Id == subId);
            sub.NextBillingDate.Should().Be(dueDate.AddMonths(1), "the date advanced so it does not re-trigger");
        }
    }

    [Fact]
    public async Task Sweep_TransitionsPastDueOpenAndPartiallyPaid_ToOverdue()
    {
        var businessId = await CreateBusinessAsync();
        var past = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var future = DateTime.UtcNow.AddDays(30);

        int openPastId, partialPastId, paidPastId, openFutureId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rail = await db.SaaSBillingMethods.Where(m => m.Code == "BankTransfer").Select(m => m.Id).FirstAsync();
            var sub = NewDueSubscription(businessId, rail, baseAmount: 14900);
            sub.NextBillingDate = null; // exclude from generation; this test is sweep-only
            db.Subscriptions.Add(sub);
            await db.SaveChangesAsync();

            openPastId = await Add(db, sub.Id, businessId, 1, SubscriptionInvoiceStatus.Open, past);
            partialPastId = await Add(db, sub.Id, businessId, 2, SubscriptionInvoiceStatus.PartiallyPaid, past);
            paidPastId = await Add(db, sub.Id, businessId, 3, SubscriptionInvoiceStatus.Paid, past);
            openFutureId = await Add(db, sub.Id, businessId, 4, SubscriptionInvoiceStatus.Open, future);
        }

        await RunSweepAsync();

        (await Status(openPastId)).Should().Be(SubscriptionInvoiceStatus.Overdue);
        (await Status(partialPastId)).Should().Be(SubscriptionInvoiceStatus.Overdue);
        (await Status(paidPastId)).Should().Be(SubscriptionInvoiceStatus.Paid, "paid invoices are never overdue");
        (await Status(openFutureId)).Should().Be(SubscriptionInvoiceStatus.Open, "not past due yet");
    }

    #region Helpers

    private static async Task<int> Add(ApplicationDbContext db, int subId, int businessId, int number,
        SubscriptionInvoiceStatus status, DateTime dueDate)
    {
        var inv = new SubscriptionInvoice
        {
            SubscriptionId = subId,
            BusinessId = businessId,
            InvoiceNumber = number,
            Status = status,
            IssuedAtUtc = dueDate.AddDays(-7),
            DueDate = dueDate,
            PeriodStart = dueDate.AddMonths(-1).AddDays(-number), // distinct per row
            PeriodEnd = dueDate,
            SubtotalCents = 14900,
            TaxCents = 0,
            TotalCents = 14900,
            Currency = "MXN"
        };
        db.SubscriptionInvoices.Add(inv);
        await db.SaveChangesAsync();
        return inv.Id;
    }

    private async Task<int> RunGenerationAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IInvoiceGenerationService>();
        return await svc.GenerateDueInvoicesAsync();
    }

    private async Task RunSweepAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IInvoiceGenerationService>();
        await svc.SweepOverdueAsync();
    }

    private async Task<int> InvoiceCount(int businessId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.SubscriptionInvoices.CountAsync(i => i.BusinessId == businessId);
    }

    private async Task<SubscriptionInvoiceStatus> Status(int invoiceId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.SubscriptionInvoices.Where(i => i.Id == invoiceId).Select(i => i.Status).FirstAsync();
    }

    private static Subscription NewDueSubscription(int businessId, int railId, int? baseAmount) => new()
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
        BaseAmountCents = baseAmount,
        Currency = "MXN",
        NextBillingDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) // in the past → due
    };

    private async Task<int> CreateBusinessAsync()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var resp = await client.PostAsJsonAsync("/api/Admin/businesses", new
        {
            businessName = $"Gen-{suffix}",
            ownerName = $"Owner {suffix}",
            email = $"gen-{suffix}@example.com",
            password = "GenPass123!",
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
