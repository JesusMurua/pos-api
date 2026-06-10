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

namespace POS.IntegrationTests.Billing;

/// <summary>
/// PR-3 SubscriptionInvoice surface: GET detail, and the void rule — only {Open, Overdue}
/// is voidable; a PartiallyPaid invoice holds recorded money and must have its payments
/// deleted first (§13). Invoices are seeded with explicit InvoiceNumber (the raw-SQL counter
/// is review-only on InMemory).
/// </summary>
public class SubscriptionInvoiceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public SubscriptionInvoiceTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_ReturnsDetail()
    {
        var (_, invoiceId, _) = await SeedInvoiceAsync(25000);

        var detail = await Admin().GetFromJsonAsync<AdminInvoiceDetailDto>($"/api/Admin/invoices/{invoiceId}");

        detail!.TotalCents.Should().Be(25000);
        detail.Status.Should().Be("Open");
        detail.PaidCents.Should().Be(0);
    }

    [Fact]
    public async Task Void_FromOpen_Succeeds()
    {
        var (_, invoiceId, _) = await SeedInvoiceAsync(25000);

        var resp = await Admin().PostAsJsonAsync($"/api/Admin/invoices/{invoiceId}/void",
            new { reason = "Issued in error" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await InvoiceStatus(invoiceId)).Should().Be(SubscriptionInvoiceStatus.Void);
    }

    [Fact]
    public async Task Void_FromPartiallyPaid_Returns400()
    {
        var (_, invoiceId, railId) = await SeedInvoiceAsync(25000);
        await Admin().PostAsJsonAsync($"/api/Admin/invoices/{invoiceId}/payments",
            new { billingMethodId = railId, amountCents = 10000, currency = "MXN" });

        var resp = await Admin().PostAsJsonAsync($"/api/Admin/invoices/{invoiceId}/void",
            new { reason = "Cannot — has money" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await InvoiceStatus(invoiceId)).Should().Be(SubscriptionInvoiceStatus.PartiallyPaid);
    }

    [Fact]
    public async Task Void_FromOverdue_Succeeds()
    {
        var (_, invoiceId, _) = await SeedInvoiceAsync(25000, status: SubscriptionInvoiceStatus.Overdue);

        var resp = await Admin().PostAsJsonAsync($"/api/Admin/invoices/{invoiceId}/void", new { reason = "Write-off" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await InvoiceStatus(invoiceId)).Should().Be(SubscriptionInvoiceStatus.Void);
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

    private async Task<(int BusinessId, int InvoiceId, int RailId)> SeedInvoiceAsync(
        int totalCents, SubscriptionInvoiceStatus status = SubscriptionInvoiceStatus.Open)
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
            Status = status,
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
            businessName = $"Inv-{suffix}",
            ownerName = $"Owner {suffix}",
            email = $"inv-{suffix}@example.com",
            password = "InvPass123!",
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
