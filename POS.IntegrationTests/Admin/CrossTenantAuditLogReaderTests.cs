using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.DTOs.Admin;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Admin;

/// <summary>
/// PR-UI-prep GAP-A: cross-tenant BusinessAuditLog reader (<c>GET /api/Admin/audit-log</c>).
/// Isolated factory per test so seeded rows are the only rows in the table.
/// </summary>
public class CrossTenantAuditLogReaderTests
{
    [Fact]
    public async Task ReturnsAllTenants_WhenNoFilter()
    {
        using var f = new CustomWebApplicationFactory();
        var bizA = await SeedBusinessAsync(f);
        var bizB = await SeedBusinessAsync(f);
        await SeedAuditAsync(f, bizA, BusinessAuditAction.PlanChanged);
        await SeedAuditAsync(f, bizB, BusinessAuditAction.Suspended);

        var page = await GetAsync(f, "/api/Admin/audit-log");

        page.TotalRows.Should().Be(2);
    }

    [Fact]
    public async Task FiltersByBusinessId()
    {
        using var f = new CustomWebApplicationFactory();
        var bizA = await SeedBusinessAsync(f);
        var bizB = await SeedBusinessAsync(f);
        await SeedAuditAsync(f, bizA, BusinessAuditAction.PlanChanged);
        await SeedAuditAsync(f, bizA, BusinessAuditAction.Suspended);
        await SeedAuditAsync(f, bizB, BusinessAuditAction.Created);

        var page = await GetAsync(f, $"/api/Admin/audit-log?businessId={bizA}");

        page.TotalRows.Should().Be(2);
        page.Items.Should().OnlyContain(i => i.BusinessId == bizA);
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
        var biz = new Business { Name = $"XAudit-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow, IsActive = true };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        return biz.Id;
    }

    private static async Task SeedAuditAsync(CustomWebApplicationFactory f, int businessId, BusinessAuditAction action)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.BusinessAuditLogs.Add(new BusinessAuditLog
        {
            BusinessId = businessId,
            Action = action,
            ChangedAtUtc = DateTime.UtcNow,
            ChangedByTokenId = "tok12345"
        });
        await db.SaveChangesAsync();
    }

    #endregion
}
