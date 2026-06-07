using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;
using POS.Services.IService;

namespace POS.IntegrationTests.Reports;

/// <summary>
/// Reproduces the real-world path the FE quick-pay uses: POST /orders/sync with a
/// payments[] array. Verifies the cash OrderPayment row is persisted (and the
/// doughnut sees it), both for a brand-new order and for a re-sync of an existing
/// order (offline → quick-pay flow).
/// </summary>
public class OrderSyncPaymentPersistenceTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public OrderSyncPaymentPersistenceTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Sync_NewOrder_WithCashPayment_PersistsOrderPaymentRow()
    {
        var (branchId, productId) = await SeedBranchAndProductAsync();
        var orderId = Guid.NewGuid().ToString();

        await SyncAsync(branchId, BuildRequest(orderId, branchId, productId));

        await AssertCashPaymentPersistedAsync(orderId, branchId);
    }

    [Fact]
    public async Task Sync_ReSync_ExistingOrder_WithCashPayment_PersistsOrderPaymentRow()
    {
        var (branchId, productId) = await SeedBranchAndProductAsync();
        var orderId = Guid.NewGuid().ToString();

        // 1st sync: order opened, unpaid, no payment yet.
        var first = BuildRequest(orderId, branchId, productId);
        first.IsPaid = false;
        first.Payments = new();
        await SyncAsync(branchId, first);

        // 2nd sync: quick-pay re-sends the SAME order id, now paid with cash.
        await SyncAsync(branchId, BuildRequest(orderId, branchId, productId));

        await AssertCashPaymentPersistedAsync(orderId, branchId);
    }

    #region Helpers

    private async Task AssertCashPaymentPersistedAsync(string orderId, int branchId)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var payments = await db.OrderPayments.Where(p => p.OrderId == orderId).ToListAsync();
            payments.Should().ContainSingle("the cash payment from the sync payload must persist");
            payments[0].Method.Should().Be(Domain.Enums.PaymentMethod.Cash);
            payments[0].AmountCents.Should().Be(50000);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
            var charts = await reports.GetDashboardChartsAsync(
                branchId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 7), "day");
            charts.SalesByPaymentMethod.Should().ContainSingle();
            charts.SalesByPaymentMethod[0].PaymentMethod.Should().Be("Cash");
            charts.SalesByPaymentMethod[0].TotalCents.Should().Be(50000);
        }
    }

    private static SyncOrderRequest BuildRequest(string orderId, int branchId, int productId) => new()
    {
        Id = orderId,
        BranchId = branchId,
        OrderNumber = 1,
        TotalCents = 50000,
        SubtotalCents = 50000,
        CreatedAt = new DateTime(2026, 6, 6, 18, 0, 0, DateTimeKind.Utc),
        IsPaid = true,
        Items = new()
        {
            new SyncOrderItemRequest { ProductId = productId, ProductName = "Uñas", Quantity = 1, UnitPriceCents = 50000 }
        },
        Payments = new()
        {
            new SyncPaymentRequest { Method = "Cash", AmountCents = 50000, Status = "completed" }
        }
    };

    private async Task SyncAsync(int branchId, SyncOrderRequest request)
    {
        using var scope = _factory.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        await orders.SyncOrdersAsync(new[] { request }, branchId);
    }

    private async Task<(int BranchId, int ProductId)> SeedBranchAndProductAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Business
        {
            Name = $"Sync-{suffix}",
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = PlanTypeIds.Pro,
            CountryCode = "MX",
            DefaultTaxId = 1, // seeded MX default tax (fiscal snapshot path)
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

        var product = new Product
        {
            BranchId = branch.Id,
            CategoryId = category.Id,
            Name = "Uñas",
            PriceCents = 50000,
            IsAvailable = true
        };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return (branch.Id, product.Id);
    }

    #endregion
}
