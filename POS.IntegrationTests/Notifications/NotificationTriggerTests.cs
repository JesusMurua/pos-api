using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Notifications;

/// <summary>
/// PR-5 trigger wiring: a representative set of lifecycle write-paths enqueue the right
/// NotificationOutbox row in the same transaction (suspend/reactivate, plan change, payment
/// received). The enqueue is best-effort and never aborts the business operation.
/// </summary>
public class NotificationTriggerTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminBusinesses = "/api/Admin/businesses";

    private readonly CustomWebApplicationFactory _factory;

    public NotificationTriggerTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Suspend_EnqueuesSuspended()
    {
        var businessId = await CreateBusinessAsync();

        var resp = await Admin().PatchAsJsonAsync($"{AdminBusinesses}/{businessId}/status",
            new { isActive = false, reason = "Falta de pago" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        (await HasOutboxAsync(businessId, "Suspended")).Should().BeTrue();
    }

    [Fact]
    public async Task Reactivate_EnqueuesReactivated()
    {
        var businessId = await CreateBusinessAsync();
        await Admin().PatchAsJsonAsync($"{AdminBusinesses}/{businessId}/status", new { isActive = false, reason = "x" });

        await Admin().PatchAsJsonAsync($"{AdminBusinesses}/{businessId}/status", new { isActive = true });

        (await HasOutboxAsync(businessId, "Reactivated")).Should().BeTrue();
    }

    [Fact]
    public async Task ChangePlan_EnqueuesPlanChanged()
    {
        var businessId = await CreateBusinessAsync();

        var resp = await Admin().PatchAsJsonAsync($"{AdminBusinesses}/{businessId}/plan",
            new { planTypeId = PlanTypeIds.Pro, reason = "Upgrade" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        (await HasOutboxAsync(businessId, "PlanChanged")).Should().BeTrue();
    }

    [Fact]
    public async Task RecordPayment_EnqueuesPaymentReceived()
    {
        var (businessId, invoiceId, railId) = await SeedInvoiceAsync();

        var resp = await Admin().PostAsJsonAsync($"/api/Admin/invoices/{invoiceId}/payments",
            new { billingMethodId = railId, amountCents = 14900, currency = "MXN" });
        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await HasOutboxAsync(businessId, "PaymentReceived")).Should().BeTrue();
    }

    #region Helpers

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        return c;
    }

    private async Task<bool> HasOutboxAsync(int businessId, string templateCode)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.NotificationOutbox.AnyAsync(n => n.BusinessId == businessId && n.TemplateCode == templateCode);
    }

    private async Task<(int BusinessId, int InvoiceId, int RailId)> SeedInvoiceAsync()
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

        var invoice = new SubscriptionInvoice
        {
            SubscriptionId = sub.Id,
            BusinessId = businessId,
            InvoiceNumber = 1,
            Status = Domain.Enums.SubscriptionInvoiceStatus.Open,
            IssuedAtUtc = DateTime.UtcNow,
            DueDate = DateTime.UtcNow.AddDays(7),
            PeriodStart = DateTime.UtcNow,
            PeriodEnd = DateTime.UtcNow.AddMonths(1),
            SubtotalCents = 14900,
            TaxCents = 0,
            TotalCents = 14900,
            Currency = "MXN"
        };
        db.SubscriptionInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (businessId, invoice.Id, railId);
    }

    private async Task<int> CreateBusinessAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var resp = await Admin().PostAsJsonAsync(AdminBusinesses, new
        {
            businessName = $"Trg-{suffix}",
            ownerName = $"Owner {suffix}",
            email = $"trg-{suffix}@example.com",
            password = "TrgPass123!",
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
