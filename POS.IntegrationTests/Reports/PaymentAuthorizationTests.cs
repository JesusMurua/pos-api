using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Domain.Models.Catalogs;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;
using POS.Services.IService;

namespace POS.IntegrationTests.Reports;

/// <summary>
/// PR-B layer-2 gating: a synced payment whose method is not enabled by the
/// tenant's plan matrix is persisted and flagged WasUnauthorized (never rejected);
/// a tenant override flips that decision either way.
/// </summary>
public class PaymentAuthorizationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PaymentAuthorizationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FreePlan_CardPayment_FlaggedUnauthorized()
    {
        var ctx = await SeedAsync(PlanTypeIds.Free);
        var orderId = await SyncOrderAsync(ctx, "Card");
        (await PaymentAsync(orderId)).WasUnauthorized.Should().BeTrue("Free plan excludes Card");
    }

    [Fact]
    public async Task FreePlan_CashPayment_Authorized()
    {
        var ctx = await SeedAsync(PlanTypeIds.Free);
        var orderId = await SyncOrderAsync(ctx, "Cash");
        (await PaymentAsync(orderId)).WasUnauthorized.Should().BeFalse("Free plan includes Cash");
    }

    [Fact]
    public async Task Override_EnablesOutOfPlanMethod_Authorized()
    {
        var ctx = await SeedAsync(PlanTypeIds.Free);
        await SeedOverrideAsync(ctx.BusinessId, "Card", enabled: true);
        var orderId = await SyncOrderAsync(ctx, "Card");
        (await PaymentAsync(orderId)).WasUnauthorized.Should().BeFalse("override enables Card despite Free plan");
    }

    [Fact]
    public async Task Override_DisablesInPlanMethod_Unauthorized()
    {
        var ctx = await SeedAsync(PlanTypeIds.Enterprise);
        await SeedOverrideAsync(ctx.BusinessId, "Card", enabled: false);
        var orderId = await SyncOrderAsync(ctx, "Card");
        (await PaymentAsync(orderId)).WasUnauthorized.Should().BeTrue("override disables Card despite Enterprise plan");
    }

    [Fact]
    public async Task DriftReport_IncludesUnauthorized()
    {
        var ctx = await SeedAsync(PlanTypeIds.Free);
        await SyncOrderAsync(ctx, "Card");

        using var scope = _factory.Services.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var report = await uow.Orders.GetFlaggedPaymentsAsync(
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1, 200);

        report.Items.Where(i => i.BusinessId == ctx.BusinessId)
            .Should().ContainSingle(i => i.WasUnauthorized && i.MethodCode == "Card");
    }

    #region Helpers

    private record Ctx(int BranchId, int BusinessId, int ProductId);

    private async Task<OrderPayment> PaymentAsync(string orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.OrderPayments.FirstAsync(p => p.OrderId == orderId);
    }

    private async Task SeedOverrideAsync(int businessId, string methodCode, bool enabled)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var methodId = await db.PaymentMethodCatalogs.Where(c => c.Code == methodCode).Select(c => c.Id).FirstAsync();
        db.TenantPaymentMethodOverrides.Add(new TenantPaymentMethodOverride
        {
            BusinessId = businessId,
            PaymentMethodId = methodId,
            IsEnabled = enabled
        });
        await db.SaveChangesAsync();
    }

    private async Task<string> SyncOrderAsync(Ctx ctx, string method)
    {
        var orderId = Guid.NewGuid().ToString();
        var request = new SyncOrderRequest
        {
            Id = orderId,
            BranchId = ctx.BranchId,
            OrderNumber = 1,
            TotalCents = 50000,
            SubtotalCents = 50000,
            CreatedAt = new DateTime(2026, 6, 6, 18, 0, 0, DateTimeKind.Utc),
            IsPaid = true,
            Items = new() { new SyncOrderItemRequest { ProductId = ctx.ProductId, ProductName = "Uñas", Quantity = 1, UnitPriceCents = 50000 } },
            Payments = new() { new SyncPaymentRequest { Method = method, AmountCents = 50000, Status = "completed" } }
        };
        using var scope = _factory.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        await orders.SyncOrdersAsync(new[] { request }, ctx.BranchId);
        return orderId;
    }

    private async Task<Ctx> SeedAsync(int planTypeId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Business
        {
            Name = $"Auth-{suffix}",
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

        var category = new Category { BranchId = branch.Id, Name = $"Cat-{suffix}", Icon = "pi-star", SortOrder = 1, IsActive = true };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var product = new Product { BranchId = branch.Id, CategoryId = category.Id, Name = "Uñas", PriceCents = 50000, IsAvailable = true };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return new Ctx(branch.Id, biz.Id, product.Id);
    }

    #endregion
}
