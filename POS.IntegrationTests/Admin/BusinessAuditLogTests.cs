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
/// Verifies that every admin action on a tenant writes a persistent
/// <c>BusinessAuditLog</c> row (PR-1a): the explicit operator trail replacing
/// the volatile Serilog-only logging. Atomic actions (suspend/plan/trial) share
/// the mutation's SaveChanges; post-success actions (create/reset/impersonate)
/// write their row separately. Suspend also persists Business.SuspensionReason.
/// </summary>
public class BusinessAuditLogTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminRoute = "/api/Admin/businesses";

    private readonly CustomWebApplicationFactory _factory;

    public BusinessAuditLogTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Create_WritesCreatedAudit_WithTokenId()
    {
        var client = Admin();
        var id = await CreateBusinessAsync(client);

        var row = await LatestAsync(id, BusinessAuditAction.Created);
        row.Should().NotBeNull();
        row!.ChangedByTokenId.Should().NotBeNullOrEmpty("the admin token id attributes the action");
    }

    [Fact]
    public async Task Suspend_WritesAudit_AndPersistsSuspensionReason()
    {
        var client = Admin();
        var id = await CreateBusinessAsync(client);

        var resp = await client.PatchAsJsonAsync($"{AdminRoute}/{id}/status",
            new AdminToggleBusinessStatusRequest { IsActive = false, Reason = "Fraude" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var row = await LatestAsync(id, BusinessAuditAction.Suspended);
        row.Should().NotBeNull();
        row!.Reason.Should().Be("Fraude");

        (await BusinessAsync(id)).SuspensionReason.Should().Be("Fraude");
    }

    [Fact]
    public async Task Reactivate_WritesAudit_AndClearsSuspensionReason()
    {
        var client = Admin();
        var id = await CreateBusinessAsync(client);

        await client.PatchAsJsonAsync($"{AdminRoute}/{id}/status",
            new AdminToggleBusinessStatusRequest { IsActive = false, Reason = "Temporal" });
        await client.PatchAsJsonAsync($"{AdminRoute}/{id}/status",
            new AdminToggleBusinessStatusRequest { IsActive = true });

        (await LatestAsync(id, BusinessAuditAction.Reactivated)).Should().NotBeNull();
        (await BusinessAsync(id)).SuspensionReason.Should().BeNull("reactivation clears the reason");
    }

    [Fact]
    public async Task ChangePlan_WritesPlanChangedAudit()
    {
        var client = Admin();
        var id = await CreateBusinessAsync(client);

        await client.PatchAsJsonAsync($"{AdminRoute}/{id}/plan",
            new AdminChangePlanRequest { PlanTypeId = PlanTypeIds.Pro, Reason = "Upsell" });

        var row = await LatestAsync(id, BusinessAuditAction.PlanChanged);
        row.Should().NotBeNull();
        row!.Reason.Should().Be("Upsell");
        row.AfterJson.Should().Contain(PlanTypeIds.Pro.ToString());
    }

    [Fact]
    public async Task ExtendTrial_WritesTrialExtendedAudit()
    {
        var client = Admin();
        var id = await CreateBusinessAsync(client);

        await client.PatchAsJsonAsync($"{AdminRoute}/{id}/trial",
            new AdminExtendTrialRequest { NewTrialEndsAt = DateTime.UtcNow.AddDays(30), Reason = "Cortesía" });

        (await LatestAsync(id, BusinessAuditAction.TrialExtended)).Should().NotBeNull();
    }

    [Fact]
    public async Task ResetPassword_WritesPasswordResetAudit()
    {
        var client = Admin();
        var id = await CreateBusinessAsync(client);

        var resp = await client.PostAsJsonAsync($"{AdminRoute}/{id}/reset-owner-password",
            new AdminResetOwnerPasswordRequest { Reason = "Lockout" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        (await LatestAsync(id, BusinessAuditAction.PasswordReset)).Should().NotBeNull();
    }

    [Fact]
    public async Task Impersonate_WritesImpersonatedAudit()
    {
        var client = Admin();
        var id = await CreateBusinessAsync(client);

        var resp = await client.PostAsJsonAsync($"{AdminRoute}/{id}/impersonate",
            new AdminImpersonateRequest { Reason = "Support session" });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var row = await LatestAsync(id, BusinessAuditAction.Impersonated);
        row.Should().NotBeNull();
        row!.Reason.Should().Be("Support session");
    }

    #region Helpers

    private HttpClient Admin()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        return client;
    }

    private static async Task<int> CreateBusinessAsync(HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var resp = await client.PostAsJsonAsync(AdminRoute, new
        {
            businessName = $"Audit-{suffix}",
            ownerName = $"Owner {suffix}",
            email = $"audit-{suffix}@example.com",
            password = "AuditPass123!",
            primaryMacroCategoryId = MacroCategoryIds.Services,
            planTypeId = PlanTypeIds.Basic,
            countryCode = "MX"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("businessId").GetInt32();
    }

    private async Task<BusinessAuditLog?> LatestAsync(int businessId, BusinessAuditAction action)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Set<BusinessAuditLog>()
            .Where(a => a.BusinessId == businessId && a.Action == action)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync();
    }

    private async Task<Business> BusinessAsync(int businessId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Businesses.IgnoreQueryFilters().FirstAsync(b => b.Id == businessId);
    }

    #endregion
}
