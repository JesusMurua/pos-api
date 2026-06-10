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
/// PR-3 TenantPayment flow on a manual rail: write-time status recompute (§8), reference
/// idempotency (M4), delete-recompute, overpayment, and currency-mismatch rejection.
/// Invoices are seeded with an explicit InvoiceNumber — the raw-SQL per-business counter is
/// not exercised by the InMemory provider (review-only, like Branch.FolioCounter).
/// </summary>
public class TenantPaymentTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TenantPaymentTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task FullPayment_TransitionsToPaid()
    {
        var (businessId, invoiceId, railId) = await SeedInvoiceAsync(totalCents: 25000);

        var resp = await Admin().PostAsJsonAsync($"/api/Admin/invoices/{invoiceId}/payments",
            new { billingMethodId = railId, amountCents = 25000, currency = "MXN" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await InvoiceStatus(invoiceId)).Should().Be(SubscriptionInvoiceStatus.Paid);
    }

    [Fact]
    public async Task PartialPayment_TransitionsToPartiallyPaid()
    {
        var (_, invoiceId, railId) = await SeedInvoiceAsync(totalCents: 25000);

        await Admin().PostAsJsonAsync($"/api/Admin/invoices/{invoiceId}/payments",
            new { billingMethodId = railId, amountCents = 10000, currency = "MXN" });

        (await InvoiceStatus(invoiceId)).Should().Be(SubscriptionInvoiceStatus.PartiallyPaid);
    }

    [Fact]
    public async Task RepeatedReference_IsIdempotent_SinglePaymentRow()
    {
        var (_, invoiceId, railId) = await SeedInvoiceAsync(totalCents: 25000);
        var body = new { billingMethodId = railId, amountCents = 10000, currency = "MXN", reference = "FOLIO-1" };

        await Admin().PostAsJsonAsync($"/api/Admin/invoices/{invoiceId}/payments", body);
        var second = await Admin().PostAsJsonAsync($"/api/Admin/invoices/{invoiceId}/payments", body);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent); // no-op, still 204

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.TenantPayments.CountAsync(p => p.InvoiceId == invoiceId)).Should().Be(1);
    }

    [Fact]
    public async Task Overpayment_MarksPaid()
    {
        var (_, invoiceId, railId) = await SeedInvoiceAsync(totalCents: 25000);

        await Admin().PostAsJsonAsync($"/api/Admin/invoices/{invoiceId}/payments",
            new { billingMethodId = railId, amountCents = 30000, currency = "MXN" });

        (await InvoiceStatus(invoiceId)).Should().Be(SubscriptionInvoiceStatus.Paid);
    }

    [Fact]
    public async Task CurrencyMismatch_Returns400()
    {
        var (_, invoiceId, railId) = await SeedInvoiceAsync(totalCents: 25000);

        var resp = await Admin().PostAsJsonAsync($"/api/Admin/invoices/{invoiceId}/payments",
            new { billingMethodId = railId, amountCents = 25000, currency = "USD" });

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeletePayment_RecomputesStatusBackToOpen()
    {
        var (_, invoiceId, railId) = await SeedInvoiceAsync(totalCents: 25000);
        await Admin().PostAsJsonAsync($"/api/Admin/invoices/{invoiceId}/payments",
            new { billingMethodId = railId, amountCents = 25000, currency = "MXN" });

        int paymentId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            paymentId = await db.TenantPayments.Where(p => p.InvoiceId == invoiceId).Select(p => p.Id).FirstAsync();
        }

        var del = await Admin().DeleteAsync($"/api/Admin/invoices/{invoiceId}/payments/{paymentId}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await InvoiceStatus(invoiceId)).Should().Be(SubscriptionInvoiceStatus.Open);
    }

    #region Helpers

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        return c;
    }

    private async Task<SubscriptionInvoiceStatus> InvoiceStatus(int invoiceId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.SubscriptionInvoices.Where(i => i.Id == invoiceId).Select(i => i.Status).FirstAsync();
    }

    private async Task<(int BusinessId, int InvoiceId, int RailId)> SeedInvoiceAsync(int totalCents)
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
            BaseAmountCents = totalCents,
            Currency = "MXN"
        };
        db.Subscriptions.Add(sub);
        await db.SaveChangesAsync();

        var invoice = new SubscriptionInvoice
        {
            SubscriptionId = sub.Id,
            BusinessId = businessId,
            InvoiceNumber = 1,
            Status = SubscriptionInvoiceStatus.Open,
            IssuedAtUtc = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(7),
            PeriodStart = DateTime.UtcNow,
            PeriodEnd = DateTime.UtcNow.AddMonths(1),
            SubtotalCents = totalCents,
            TaxCents = 0,
            TotalCents = totalCents,
            Currency = "MXN"
        };
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();

        return (businessId, invoice.Id, railId);
    }

    private async Task<int> CreateBusinessAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var resp = await Admin().PostAsJsonAsync("/api/Admin/businesses", new
        {
            businessName = $"Pay-{suffix}",
            ownerName = $"Owner {suffix}",
            email = $"pay-{suffix}@example.com",
            password = "PayPass123!",
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
