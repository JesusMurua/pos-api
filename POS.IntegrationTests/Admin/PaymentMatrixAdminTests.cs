using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.DTOs.Admin;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Domain.Models.Catalogs;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Admin;

/// <summary>
/// Admin (X-Admin-Token) CRUD over the payment-method catalog, plan matrix,
/// overrides, preview-impact and audit log.
/// </summary>
public class PaymentMatrixAdminTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PaymentMatrixAdminTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Admin_NoToken_401()
    {
        var resp = await _factory.CreateClient().GetAsync("/api/Admin/payment-method-catalog");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Admin_GetCatalog_ReturnsNineSystemMethods()
    {
        var catalog = await Admin().GetFromJsonAsync<List<PaymentMethodCatalogDto>>("/api/Admin/payment-method-catalog", JsonOpts);
        catalog!.Should().HaveCountGreaterThanOrEqualTo(9);
        catalog.Where(c => c.IsSystem).Should().HaveCount(9);
    }

    [Fact]
    public async Task Admin_DeleteSystemMethod_409()
    {
        var cashId = await MethodIdAsync("Cash");
        var resp = await Admin().DeleteAsync($"/api/Admin/payment-method-catalog/{cashId}");
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict, "system methods cannot be deleted");
    }

    [Fact]
    public async Task Admin_CreateThenHardDelete_WhenNoPayments()
    {
        var admin = Admin();
        var code = "Crypto" + Guid.NewGuid().ToString("N")[..6];
        var create = await admin.PostAsJsonAsync("/api/Admin/payment-method-catalog", NewMethod(code));
        create.EnsureSuccessStatusCode();
        var dto = await create.Content.ReadFromJsonAsync<PaymentMethodCatalogDto>(JsonOpts);

        var del = await admin.DeleteAsync($"/api/Admin/payment-method-catalog/{dto!.Id}");
        del.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.PaymentMethodCatalogs.AnyAsync(c => c.Id == dto.Id)).Should().BeFalse("hard-deleted (no payments)");
    }

    [Fact]
    public async Task Admin_SoftDelete_WhenMethodHasPayments()
    {
        var admin = Admin();
        var code = "Crypto" + Guid.NewGuid().ToString("N")[..6];
        var create = await admin.PostAsJsonAsync("/api/Admin/payment-method-catalog", NewMethod(code));
        var dto = await create.Content.ReadFromJsonAsync<PaymentMethodCatalogDto>(JsonOpts);
        await SeedPaymentReferencingAsync(dto!.Id, dto.Code);

        var del = await admin.DeleteAsync($"/api/Admin/payment-method-catalog/{dto.Id}");
        del.EnsureSuccessStatusCode();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = await db.PaymentMethodCatalogs.FirstOrDefaultAsync(c => c.Id == dto.Id);
        row.Should().NotBeNull("soft-deleted, not removed");
        row!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Admin_CreateCatalog_InvalidSatCode_400()
    {
        var resp = await Admin().PostAsJsonAsync("/api/Admin/payment-method-catalog",
            NewMethod("Bad" + Guid.NewGuid().ToString("N")[..6]) with { SatPaymentFormCode = "00" });
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest, "00 is not a SAT c_FormaPago code");
    }

    [Fact]
    public async Task Admin_PreviewImpact_ExcludesOverriddenTenants()
    {
        var cardId = await MethodIdAsync("Card");
        var bizA = await SeedBusinessAsync(PlanTypeIds.Pro);
        var bizB = await SeedBusinessAsync(PlanTypeIds.Pro);
        await SeedOverrideAsync(bizB, cardId);

        var preview = await Admin().GetFromJsonAsync<PaymentPreviewImpactDto>(
            $"/api/Admin/payment-matrix/preview-impact?paymentMethodId={cardId}&planTypeId={PlanTypeIds.Pro}&enabled=false");

        var ids = preview!.AffectedTenants.Select(t => t.Id).ToList();
        ids.Should().Contain(bizA);
        ids.Should().NotContain(bizB, "a tenant with an override is shielded from the plan change");
    }

    [Fact]
    public async Task Admin_AuditLog_RecordsCatalogMutation()
    {
        var admin = Admin();
        var code = "Crypto" + Guid.NewGuid().ToString("N")[..6];
        await admin.PostAsJsonAsync("/api/Admin/payment-method-catalog", NewMethod(code));

        var log = await admin.GetFromJsonAsync<PagedPaymentAuditLogDto>(
            "/api/Admin/payment-matrix/audit-log?axis=catalog&pageSize=200");
        log!.Items.Should().Contain(e => e.Axis == "catalog" && e.EntityKey.Contains(code));
    }

    #region Helpers

    private static readonly JsonSerializerOptions JsonOpts =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private HttpClient Admin()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        return c;
    }

    private static UpsertPaymentMethodCatalogRequest NewMethod(string code) => new(
        Code: code, Name: "Cripto", SortOrder: 50, Category: PaymentCategory.Other,
        SatPaymentFormCode: "99", RequiresReference: false, RequiresCustomer: false,
        SupportsOverpay: false, SupportsPartial: true, ProviderKey: null,
        CountryCode: null, IconClass: null, IsActive: true);

    private async Task<int> MethodIdAsync(string code)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.PaymentMethodCatalogs.Where(c => c.Code == code).Select(c => c.Id).FirstAsync();
    }

    private async Task<int> SeedBusinessAsync(int planTypeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var biz = new Business
        {
            Name = $"Prev-{Guid.NewGuid():N}"[..20],
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = planTypeId,
            CountryCode = "MX",
            DefaultTaxId = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        return biz.Id;
    }

    private async Task SeedOverrideAsync(int businessId, int methodId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.TenantPaymentMethodOverrides.Add(new TenantPaymentMethodOverride
        {
            BusinessId = businessId,
            PaymentMethodId = methodId,
            IsEnabled = false
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedPaymentReferencingAsync(int methodId, string methodCode)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Business { Name = $"Pay-{suffix}", PrimaryMacroCategoryId = MacroCategoryIds.Services, PlanTypeId = PlanTypeIds.Pro, CountryCode = "MX", DefaultTaxId = 1, IsActive = true, CreatedAt = DateTime.UtcNow };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        var branch = new Branch { BusinessId = biz.Id, Name = $"B-{suffix}", IsMatrix = true, IsActive = true, FolioCounter = 0, TimeZoneId = "America/Mexico_City", CreatedAt = DateTime.UtcNow };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            BranchId = branch.Id,
            OrderNumber = 1,
            TotalCents = 1000,
            PaidCents = 1000,
            IsPaid = true,
            CreatedAt = DateTime.UtcNow,
            Payments = new List<OrderPayment>
            {
                new()
                {
                    Method = PaymentMethod.Other,
                    MethodCode = methodCode,
                    Category = PaymentCategory.Other,
                    SatPaymentFormCode = "99",
                    PaymentMethodId = methodId,
                    AmountCents = 1000,
                    PaymentStatusId = PaymentStatus.Completed,
                    CreatedAt = DateTime.UtcNow
                }
            }
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }

    #endregion
}
