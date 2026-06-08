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
/// Multi-method payment behavior: change comes only from CASH overpayment, the
/// summary buckets group by payment category, and the cash-register close counts
/// only net cash. All exercised end-to-end through /orders/sync so
/// RecalculatePaymentTotals computes ChangeCents.
/// </summary>
public class MultiMethodPaymentTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Tz = "America/Mexico_City";
    private static readonly DateTime CreatedUtc = new(2026, 6, 6, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly LocalDay = new(2026, 6, 6);

    private readonly CustomWebApplicationFactory _factory;

    public MultiMethodPaymentTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CashPlusCard_ExactTotal_NoChange_BucketsSplit()
    {
        var ctx = await SeedAsync();
        var orderId = await SyncOrderAsync(ctx, total: 50000, payments: new[]
        {
            ("Cash", 30000), ("Card", 20000)
        });

        (await ChangeCentsAsync(orderId)).Should().Be(0);
        var sales = await SummaryAsync(ctx.BranchId);
        sales.CashCents.Should().Be(30000);
        sales.CardCents.Should().Be(20000);
        (sales.CashCents + sales.CardCents).Should().Be(sales.TotalCents);
    }

    [Fact]
    public async Task CardOverpay_NoCash_NoPhantomChange_NoCashContamination()
    {
        // $600 card on a $500 order — impossible via a real terminal (it charges
        // exact) and the FE caps non-cash; the backend must NOT invent change nor
        // pollute the cash bucket. Card shows what was charged ($600).
        var ctx = await SeedAsync();
        var orderId = await SyncOrderAsync(ctx, total: 50000, payments: new[] { ("Card", 60000) });

        (await ChangeCentsAsync(orderId)).Should().Be(0, "no cash → no change");
        var sales = await SummaryAsync(ctx.BranchId);
        sales.CashCents.Should().Be(0, "cash bucket must not go negative");
        sales.CardCents.Should().Be(60000, "card bucket shows the amount charged, no silent proration");
    }

    [Fact]
    public async Task CashOverpay_PlusCard_ChangeFromCashOnly()
    {
        // Cash $600 + Card $200 on $500 → cash applies $300, change $300 (cash),
        // card $200.
        var ctx = await SeedAsync();
        var orderId = await SyncOrderAsync(ctx, total: 50000, payments: new[]
        {
            ("Cash", 60000), ("Card", 20000)
        });

        (await ChangeCentsAsync(orderId)).Should().Be(30000);
        var sales = await SummaryAsync(ctx.BranchId);
        sales.CashCents.Should().Be(30000, "cash net of the $300 change");
        sales.CardCents.Should().Be(20000);
    }

    [Fact]
    public async Task Buckets_ByCategory_Digital_Card_FromFrozenCategory()
    {
        var ctx = await SeedAsync();
        await SyncOrderAsync(ctx, total: 60000, payments: new[]
        {
            ("Transfer", 20000), ("Clip", 20000), ("MercadoPago", 20000)
        });

        var sales = await SummaryAsync(ctx.BranchId);
        sales.CardCents.Should().Be(20000, "Clip folds into the Card category");
        sales.DigitalCents.Should().Be(40000, "Transfer + MercadoPago are the Digital category");
        sales.TransferCents.Should().Be(40000, "deprecated alias mirrors DigitalCents");
        sales.OtherCents.Should().Be(0);
        sales.CashCents.Should().Be(0);
    }

    [Fact]
    public async Task Sync_FreezesCatalogSnapshot_OnEachPayment()
    {
        var ctx = await SeedAsync();
        var orderId = await SyncOrderAsync(ctx, total: 50000, payments: new[] { ("Card", 50000) });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var payment = await db.OrderPayments.FirstAsync(p => p.OrderId == orderId);

        payment.MethodCode.Should().Be("Card");
        payment.Category.Should().Be(PaymentCategory.Card);
        payment.SatPaymentFormCode.Should().Be("04");
        payment.PaymentMethodId.Should().BeGreaterThan(0, "FK resolved from the catalog");
    }

    [Fact]
    public async Task CloseSession_MultiMethod_CountsOnlyNetCash()
    {
        var ctx = await SeedAsync();
        var sessionId = await SeedOpenSessionAsync(ctx.BranchId);
        // Cash $600 + Card $200 on $500 → drawer gets $300 net cash (card excluded).
        await SyncOrderAsync(ctx, total: 50000,
            payments: new[] { ("Cash", 60000), ("Card", 20000) }, sessionId: sessionId);

        using var scope = _factory.Services.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<ICashRegisterService>();
        var closed = await cash.CloseSessionAsync(ctx.BranchId, ctx.UserId,
            new CloseSessionRequest { CountedAmountCents = 30000 });

        closed.CashSalesCents.Should().Be(30000, "only net cash, excludes the $200 card");
        closed.DifferenceCents.Should().Be(0);
    }

    #region Helpers

    private record Ctx(int BranchId, int ProductId, int UserId);

    private async Task<int> ChangeCentsAsync(string orderId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Orders.FirstAsync(o => o.Id == orderId)).ChangeCents;
    }

    private async Task<DashboardSales> SummaryAsync(int branchId)
    {
        using var scope = _factory.Services.CreateScope();
        var dashboard = scope.ServiceProvider.GetRequiredService<IDashboardService>();
        return (await dashboard.GetSummaryAsync(branchId, LocalDay)).Sales;
    }

    private async Task<string> SyncOrderAsync(Ctx ctx, int total, (string Method, int Amount)[] payments, int? sessionId = null)
    {
        var orderId = Guid.NewGuid().ToString();
        var request = new SyncOrderRequest
        {
            Id = orderId,
            BranchId = ctx.BranchId,
            OrderNumber = 1,
            TotalCents = total,
            SubtotalCents = total,
            CreatedAt = CreatedUtc,
            IsPaid = true,
            CashRegisterSessionId = sessionId,
            Items = new() { new SyncOrderItemRequest { ProductId = ctx.ProductId, ProductName = "Uñas", Quantity = 1, UnitPriceCents = total } },
            Payments = payments.Select(p => new SyncPaymentRequest { Method = p.Method, AmountCents = p.Amount, Status = "completed" }).ToList()
        };

        using var scope = _factory.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
        await orders.SyncOrdersAsync(new[] { request }, ctx.BranchId);
        return orderId;
    }

    private async Task<int> SeedOpenSessionAsync(int branchId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = new CashRegisterSession
        {
            BranchId = branchId,
            OpenedAt = DateTime.UtcNow,
            InitialAmountCents = 0,
            CashRegisterStatusId = CashRegisterStatus.Open
        };
        db.CashRegisterSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private async Task<Ctx> SeedAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Business
        {
            Name = $"Multi-{suffix}",
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
            TimeZoneId = Tz,
            CreatedAt = DateTime.UtcNow
        };
        db.Branches.Add(branch);
        await db.SaveChangesAsync();

        var owner = new User
        {
            BusinessId = biz.Id,
            BranchId = branch.Id,
            Name = $"Owner-{suffix}",
            Email = $"multi-{suffix}@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("MultiPass123!"),
            RoleId = UserRoleIds.Owner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var category = new Category { BranchId = branch.Id, Name = $"Cat-{suffix}", Icon = "pi-star", SortOrder = 1, IsActive = true };
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        var product = new Product { BranchId = branch.Id, CategoryId = category.Id, Name = "Uñas", PriceCents = 50000, IsAvailable = true };
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return new Ctx(branch.Id, product.Id, owner.Id);
    }

    #endregion
}
