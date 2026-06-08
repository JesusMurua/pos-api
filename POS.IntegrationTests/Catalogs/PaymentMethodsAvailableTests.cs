using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.DTOs.Admin;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Domain.Models.Catalogs;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Catalogs;

/// <summary>
/// Public <c>GET /api/payment-methods/available</c>: tenant-authed, filtered by
/// plan matrix + per-business override + country, with per-tenant cache that an
/// admin mutation invalidates.
/// </summary>
public class PaymentMethodsAvailableTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PaymentMethodsAvailableTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Available_RequiresAuth()
    {
        var resp = await _factory.CreateClient().GetAsync("/api/payment-methods/available");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Available_FreePlan_OnlyCashAndOther()
    {
        var ctx = await SeedAsync(PlanTypeIds.Free);
        var items = await GetAvailableAsync(ctx);

        items.Select(i => i.Code).Should().BeEquivalentTo(new[] { "Cash", "Other" });
    }

    [Fact]
    public async Task Available_OverrideEnablesAndRelabels()
    {
        var ctx = await SeedAsync(PlanTypeIds.Free);
        await SeedOverrideAsync(ctx.BusinessId, "Card", enabled: true, customLabel: "Mi TPV");

        var items = await GetAvailableAsync(ctx);
        var card = items.SingleOrDefault(i => i.Code == "Card");
        card.Should().NotBeNull("override enables Card on a Free plan");
        card!.Name.Should().Be("Mi TPV", "custom label surfaces as name");
    }

    [Fact]
    public async Task Available_CacheInvalidatedAfterAdminEnable()
    {
        var ctx = await SeedAsync(PlanTypeIds.Free);
        (await GetAvailableAsync(ctx)).Select(i => i.Code).Should().NotContain("Card");

        // Admin enables Card for the Free plan → bump invalidates the per-tenant cache.
        var cardId = await MethodIdAsync("Card");
        var admin = _factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        var put = await admin.PutAsJsonAsync("/api/Admin/plan-payment-method-matrix",
            new[] { new PlanPaymentMethodEntryDto(PlanTypeIds.Free, cardId, true) });
        put.EnsureSuccessStatusCode();

        (await GetAvailableAsync(ctx)).Select(i => i.Code)
            .Should().Contain("Card", "the cache was invalidated, not served stale");
    }

    #region Helpers

    private record Ctx(int BranchId, int BusinessId, int UserId);
    private record AvailableItem(int Id, string Code, string Name, string Category,
        bool SupportsOverpay, bool RequiresReference, bool RequiresCustomer,
        string? ProviderKey, string? Icon, int SortOrder);

    private async Task<List<AvailableItem>> GetAvailableAsync(Ctx ctx)
    {
        var client = _factory.CreateClient();
        var token = JwtTestFactory.CreateUserToken(ctx.BusinessId, ctx.BranchId, ctx.UserId, "Owner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.GetFromJsonAsync<List<AvailableItem>>("/api/payment-methods/available"))!;
    }

    private async Task<int> MethodIdAsync(string code)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.PaymentMethodCatalogs.Where(c => c.Code == code).Select(c => c.Id).FirstAsync();
    }

    private async Task SeedOverrideAsync(int businessId, string code, bool enabled, string? customLabel)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var methodId = await db.PaymentMethodCatalogs.Where(c => c.Code == code).Select(c => c.Id).FirstAsync();
        db.TenantPaymentMethodOverrides.Add(new TenantPaymentMethodOverride
        {
            BusinessId = businessId,
            PaymentMethodId = methodId,
            IsEnabled = enabled,
            CustomLabel = customLabel
        });
        await db.SaveChangesAsync();
    }

    private async Task<Ctx> SeedAsync(int planTypeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Business
        {
            Name = $"Avail-{suffix}",
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = planTypeId,
            CountryCode = "MX",
            DefaultTaxId = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();

        var branch = new Branch
        {
            BusinessId = biz.Id,
            Name = $"Matrix-{suffix}",
            IsMatrix = true,
            IsActive = true,
            FolioCounter = 0,
            TimeZoneId = "America/Mexico_City",
            CreatedAt = DateTime.UtcNow
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var owner = new User
        {
            BusinessId = biz.Id,
            BranchId = branch.Id,
            Name = $"Owner-{suffix}",
            Email = $"avail-{suffix}@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("AvailPass123!"),
            RoleId = UserRoleIds.Owner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        return new Ctx(branch.Id, biz.Id, owner.Id);
    }

    #endregion
}
