using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.DTOs.Admin;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Admin;

/// <summary>
/// PR-UI-prep GAP-A: per-tenant BusinessAuditLog reader. Isolated factory per test
/// (the audit log is a global, cross-tenant table — pollution-proof via own DB).
/// </summary>
public class BusinessAuditLogReaderTests
{
    [Fact]
    public async Task ReturnsTenantRows_NewestFirst()
    {
        using var f = new CustomWebApplicationFactory();
        var bizId = await SeedBusinessAsync(f);
        await SeedAuditAsync(f, bizId, BusinessAuditAction.Created, daysAgo: 3);
        await SeedAuditAsync(f, bizId, BusinessAuditAction.PlanChanged, daysAgo: 2);
        await SeedAuditAsync(f, bizId, BusinessAuditAction.Suspended, daysAgo: 1);

        var page = await GetAsync(f, $"/api/Admin/businesses/{bizId}/audit-log");

        page.TotalRows.Should().Be(3);
        page.Items.Should().HaveCount(3);
        page.Items[0].Action.Should().Be("Suspended"); // newest first
        page.Items[2].Action.Should().Be("Created");
        page.Items[0].BusinessId.Should().Be(bizId);
    }

    [Fact]
    public async Task FiltersByAction()
    {
        using var f = new CustomWebApplicationFactory();
        var bizId = await SeedBusinessAsync(f);
        await SeedAuditAsync(f, bizId, BusinessAuditAction.PlanChanged, daysAgo: 3);
        await SeedAuditAsync(f, bizId, BusinessAuditAction.PlanChanged, daysAgo: 2);
        await SeedAuditAsync(f, bizId, BusinessAuditAction.Suspended, daysAgo: 1);

        var page = await GetAsync(f, $"/api/Admin/businesses/{bizId}/audit-log?action=PlanChanged");

        page.TotalRows.Should().Be(2);
        page.Items.Should().OnlyContain(i => i.Action == "PlanChanged");
    }

    [Fact]
    public async Task UnknownAction_YieldsZeroRows()
    {
        using var f = new CustomWebApplicationFactory();
        var bizId = await SeedBusinessAsync(f);
        await SeedAuditAsync(f, bizId, BusinessAuditAction.PlanChanged, daysAgo: 1);

        var page = await GetAsync(f, $"/api/Admin/businesses/{bizId}/audit-log?action=NotARealAction");

        page.TotalRows.Should().Be(0); // typed filter: no silent wildcard
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task FiltersByDateRange()
    {
        using var f = new CustomWebApplicationFactory();
        var bizId = await SeedBusinessAsync(f);
        await SeedAuditAsync(f, bizId, BusinessAuditAction.Created, daysAgo: 10);
        await SeedAuditAsync(f, bizId, BusinessAuditAction.PlanChanged, daysAgo: 5);
        await SeedAuditAsync(f, bizId, BusinessAuditAction.Suspended, daysAgo: 1);

        var from = DateTime.UtcNow.AddDays(-6).ToString("O");
        var to = DateTime.UtcNow.AddDays(-2).ToString("O");
        var page = await GetAsync(f, $"/api/Admin/businesses/{bizId}/audit-log?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}");

        page.TotalRows.Should().Be(1);
        page.Items.Single().Action.Should().Be("PlanChanged");
    }

    [Fact]
    public async Task Paginates()
    {
        using var f = new CustomWebApplicationFactory();
        var bizId = await SeedBusinessAsync(f);
        for (var i = 0; i < 5; i++)
            await SeedAuditAsync(f, bizId, BusinessAuditAction.PlanChanged, daysAgo: 5 - i);

        var p1 = await GetAsync(f, $"/api/Admin/businesses/{bizId}/audit-log?page=1&pageSize=2");
        var p2 = await GetAsync(f, $"/api/Admin/businesses/{bizId}/audit-log?page=2&pageSize=2");
        var p3 = await GetAsync(f, $"/api/Admin/businesses/{bizId}/audit-log?page=3&pageSize=2");

        p1.TotalRows.Should().Be(5);
        p1.Items.Should().HaveCount(2);
        p2.Items.Should().HaveCount(2);
        p3.Items.Should().HaveCount(1);
    }

    #region Helpers

    private static HttpClient Admin(CustomWebApplicationFactory f)
    {
        var c = f.CreateClient();
        c.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        return c;
    }

    private static async Task<PagedBusinessAuditLogDto> GetAsync(CustomWebApplicationFactory f, string url) =>
        (await Admin(f).GetFromJsonAsync<PagedBusinessAuditLogDto>(url))!;

    private static async Task<int> SeedBusinessAsync(CustomWebApplicationFactory f)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var biz = new Business { Name = $"Audit-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow, IsActive = true };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        return biz.Id;
    }

    private static async Task SeedAuditAsync(CustomWebApplicationFactory f, int businessId, BusinessAuditAction action, int daysAgo)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.BusinessAuditLogs.Add(new BusinessAuditLog
        {
            BusinessId = businessId,
            Action = action,
            ChangedAtUtc = DateTime.UtcNow.AddDays(-daysAgo),
            ChangedByTokenId = "tok12345",
            Reason = $"seed {action}"
        });
        await db.SaveChangesAsync();
    }

    #endregion
}
