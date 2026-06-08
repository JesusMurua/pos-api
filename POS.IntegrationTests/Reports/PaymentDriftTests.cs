using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;
using POS.Services.IService;

namespace POS.IntegrationTests.Reports;

/// <summary>
/// PR-A2 soft gating: unknown payment methods are recorded as Other and flagged
/// (never rejected), and the cross-tenant drift report surfaces only flagged
/// payments. WasUnauthorized arrives with PR-B (plan matrix).
/// </summary>
public class PaymentDriftTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public PaymentDriftTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Sync_UnknownMethod_PersistsAsOther_AndFlags()
    {
        var ctx = await SeedAsync();
        var orderId = await SyncOrderAsync(ctx, "FooBar");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.OrderPayments.FirstAsync(p => p.OrderId == orderId);

        payment.Method.Should().Be(PaymentMethod.Other, "unknown methods fall back to Other");
        payment.MethodCode.Should().Be("Other");
        payment.WasUnknownMethod.Should().BeTrue();
        payment.WasUnauthorized.Should().BeFalse("unauthorized gating is PR-B");
    }

    [Fact]
    public async Task DriftReport_ReturnsOnlyFlaggedPayments()
    {
        var ctx = await SeedAsync();
        await SyncOrderAsync(ctx, "FooBar");   // flagged
        await SyncOrderAsync(ctx, "Cash");     // clean

        var report = await GetDriftAsync();
        var mine = report!.Items.Where(i => i.BusinessId == ctx.BusinessId).ToList();

        mine.Should().ContainSingle("only the unknown-method payment is flagged");
        mine[0].WasUnknownMethod.Should().BeTrue();
        mine[0].MethodCode.Should().Be("Other");
        mine[0].BusinessName.Should().Be(ctx.BusinessName);
        mine[0].PlanType.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task NormalCashOrder_NotInDrift()
    {
        var ctx = await SeedAsync();
        await SyncOrderAsync(ctx, "Cash");

        var report = await GetDriftAsync();
        report!.Items.Where(i => i.BusinessId == ctx.BusinessId)
            .Should().BeEmpty("a clean cash order has no flags");
    }

    #region Helpers

    private record Ctx(int BranchId, int BusinessId, string BusinessName, int ProductId);

    private async Task<DriftReport?> GetDriftAsync()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        return await client.GetFromJsonAsync<DriftReport>(
            "/api/Admin/orders/unauthorized-methods?from=2020-01-01T00:00:00Z&to=2030-01-01T00:00:00Z&pageSize=200");
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

    private async Task<Ctx> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Business
        {
            Name = $"Drift-{suffix}",
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = PlanTypeIds.Pro,
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

        return new Ctx(branch.Id, biz.Id, biz.Name, product.Id);
    }

    // Local mirrors of the response records (deserialization target).
    private record DriftReport(int Page, int PageSize, int TotalRows, List<DriftItem> Items);
    private record DriftItem(
        string OrderId, int OrderNumber, int BusinessId, string BusinessName, string PlanType,
        string MethodCode, string MethodName, string MethodCategory,
        bool WasUnauthorized, bool WasUnknownMethod, DateTime CreatedAt, int AmountCents);

    #endregion
}
