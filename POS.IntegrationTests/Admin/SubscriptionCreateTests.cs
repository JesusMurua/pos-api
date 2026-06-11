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
/// MINI-PR: POST /Admin/businesses/{id}/subscription on a MANUAL rail (BankTransfer) — no Stripe
/// call. Persists the local row + BusinessAuditLog(SubscriptionCreated), keeps Business.PlanTypeId
/// in sync, surfaces empty Stripe ids as null. (The Stripe-rail SDK path is runtime-validated at the
/// end of the roadmap — same discipline as reconcile/add-on.) Plus the 409 + 400 guards.
/// </summary>
public class SubscriptionCreateTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminBusinesses = "/api/Admin/businesses";

    private readonly CustomWebApplicationFactory _factory;

    public SubscriptionCreateTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task ManualRail_Create_Returns201_PersistsAndAudits()
    {
        var businessId = await CreateBusinessAsync();
        var bankTransferId = await BillingMethodIdAsync("BankTransfer");

        var resp = await Admin().PostAsJsonAsync($"{AdminBusinesses}/{businessId}/subscription",
            new AdminCreateSubscriptionRequest
            {
                PlanTypeId = PlanTypeIds.Basic,
                BillingMethodId = bankTransferId,
                BaseAmountCents = 14900,
                CfdiRequired = true,
                BillingEmail = "owner@x.com",
                Reason = "Alta manual"
            });

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var detail = await resp.Content.ReadFromJsonAsync<AdminSubscriptionDetailDto>();
        detail!.PlanTypeId.Should().Be(PlanTypeIds.Basic);
        detail.BillingMethodCode.Should().Be("BankTransfer");
        detail.BaseAmountCents.Should().Be(14900);
        detail.Status.Should().Be("active");
        detail.StripeCustomerId.Should().BeNull("manual rail has no Stripe customer");
        detail.StripeSubscriptionId.Should().BeNull();
        detail.NextBillingDate.Should().NotBeNull("manual rail drives the local generation job");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var sub = await db.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.BusinessId == businessId);
        sub.BillingMethodId.Should().Be(bankTransferId);
        sub.CfdiRequired.Should().BeTrue();

        var business = await db.Businesses.IgnoreQueryFilters().FirstAsync(b => b.Id == businessId);
        business.PlanTypeId.Should().Be(PlanTypeIds.Basic, "Business.PlanTypeId is the feature-gate SSoT");

        (await db.Set<BusinessAuditLog>()
            .AnyAsync(a => a.BusinessId == businessId && a.Action == BusinessAuditAction.SubscriptionCreated))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Create_Conflict409_WhenSubscriptionExists()
    {
        var businessId = await CreateBusinessAsync();
        var bankTransferId = await BillingMethodIdAsync("BankTransfer");

        var first = await PostAsync(businessId, bankTransferId, PlanTypeIds.Basic);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var second = await PostAsync(businessId, bankTransferId, PlanTypeIds.Pro);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_400_UnknownPlan()
    {
        var businessId = await CreateBusinessAsync();
        var bankTransferId = await BillingMethodIdAsync("BankTransfer");

        var resp = await PostAsync(businessId, bankTransferId, planTypeId: 9999);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_400_UnknownBillingMethod()
    {
        var businessId = await CreateBusinessAsync();

        var resp = await PostAsync(businessId, billingMethodId: 9999, PlanTypeIds.Basic);
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_400_NegativeBaseAmount()
    {
        var businessId = await CreateBusinessAsync();
        var bankTransferId = await BillingMethodIdAsync("BankTransfer");

        var resp = await Admin().PostAsJsonAsync($"{AdminBusinesses}/{businessId}/subscription",
            new AdminCreateSubscriptionRequest
            {
                PlanTypeId = PlanTypeIds.Basic,
                BillingMethodId = bankTransferId,
                BaseAmountCents = -1
            });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #region Helpers

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        return c;
    }

    private Task<HttpResponseMessage> PostAsync(int businessId, int billingMethodId, int planTypeId) =>
        Admin().PostAsJsonAsync($"{AdminBusinesses}/{businessId}/subscription",
            new AdminCreateSubscriptionRequest { PlanTypeId = planTypeId, BillingMethodId = billingMethodId });

    private async Task<int> BillingMethodIdAsync(string code)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.SaaSBillingMethods.Where(m => m.Code == code).Select(m => m.Id).FirstAsync();
    }

    private async Task<int> CreateBusinessAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var resp = await Admin().PostAsJsonAsync(AdminBusinesses, new
        {
            businessName = $"Create-{suffix}",
            ownerName = $"Owner {suffix}",
            email = $"create-{suffix}@example.com",
            password = "CreatePass123!",
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
