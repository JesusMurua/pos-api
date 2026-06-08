using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;
using POS.Services.IService;

namespace POS.IntegrationTests.Reports;

/// <summary>
/// Locks the three dashboard/caja fixes:
/// (1) cash buckets are net of change in the summary and the cash-register close,
/// (2) salesOverTime buckets by the branch's local day, not UTC,
/// (3) IsPaid is computed server-side so the BI charts include real paid sales.
/// </summary>
public class DashboardFixesTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Tz = "America/Mexico_City"; // GMT-6 (no DST in this id)

    private readonly CustomWebApplicationFactory _factory;

    public DashboardFixesTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Summary_Cash_IsNetOfChange()
    {
        var branchId = await SeedBranchAsync();
        // $500 sale, paid with $600 cash → $100 change.
        await SeedPaidCashOrderAsync(branchId, totalCents: 50000, tenderedCents: 60000,
            createdAtUtc: new DateTime(2026, 6, 6, 18, 0, 0, DateTimeKind.Utc));

        using var scope = _factory.Services.CreateScope();
        var dashboard = scope.ServiceProvider.GetRequiredService<IDashboardService>();
        var summary = await dashboard.GetSummaryAsync(branchId, new DateOnly(2026, 6, 6));

        summary.Sales.CashCents.Should().Be(50000, "cash is net of the $100 change returned");
        summary.Sales.TotalCents.Should().Be(50000);
        (summary.Sales.CashCents + summary.Sales.CardCents)
            .Should().Be(summary.Sales.TotalCents, "net cash + card must reconcile to total");
    }

    [Fact]
    public async Task SalesOverTime_BucketsBy_LocalDay_NotUtc()
    {
        var branchId = await SeedBranchAsync();
        // 2026-06-07 04:00 UTC == 2026-06-06 22:00 local (GMT-6) → must bucket on the 6th.
        await SeedPaidCashOrderAsync(branchId, totalCents: 50000, tenderedCents: 50000,
            createdAtUtc: new DateTime(2026, 6, 7, 4, 0, 0, DateTimeKind.Utc));

        using var scope = _factory.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var charts = await reports.GetDashboardChartsAsync(
            branchId, new DateOnly(2026, 6, 6), new DateOnly(2026, 6, 6), "day");

        charts.SalesOverTime.Should().ContainSingle();
        var point = charts.SalesOverTime[0];
        point.Date.Day.Should().Be(6, "the late-night local sale belongs to the 6th, not the 7th UTC");
        point.Date.Month.Should().Be(6);
    }

    [Fact]
    public async Task CloseSession_CashSales_NetOfChange_NoFalseShortage()
    {
        var (branchId, userId) = await SeedBranchWithOwnerAsync();
        var sessionId = await SeedOpenSessionAsync(branchId);
        // $500 sale paid with $600 cash → $100 change, linked to the session.
        await SeedPaidCashOrderAsync(branchId, totalCents: 50000, tenderedCents: 60000,
            createdAtUtc: new DateTime(2026, 6, 6, 18, 0, 0, DateTimeKind.Utc), sessionId: sessionId);

        using var scope = _factory.Services.CreateScope();
        var cash = scope.ServiceProvider.GetRequiredService<ICashRegisterService>();
        // Drawer actually holds the $500 net (the $100 change left the drawer).
        var closed = await cash.CloseSessionAsync(branchId, userId,
            new CloseSessionRequest { CountedAmountCents = 50000 });

        closed.CashSalesCents.Should().Be(50000, "cash sales are net of change returned");
        closed.DifferenceCents.Should().Be(0, "no false shortage — expected matches the net drawer");
    }

    [Fact]
    public async Task SalesByPaymentMethod_CashOnly_ReturnsSingleSlice()
    {
        // Two $500 cash sales WITH payment rows → one Cash slice of $1,000, count 2.
        var branchId = await SeedBranchAsync();
        await SeedPaidCashOrderAsync(branchId, 50000, 50000, new DateTime(2026, 6, 5, 18, 0, 0, DateTimeKind.Utc));
        await SeedPaidCashOrderAsync(branchId, 50000, 50000, new DateTime(2026, 6, 6, 18, 0, 0, DateTimeKind.Utc));

        using var scope = _factory.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var charts = await reports.GetDashboardChartsAsync(
            branchId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 7), "day");

        charts.SalesByPaymentMethod.Should().ContainSingle();
        var slice = charts.SalesByPaymentMethod[0];
        slice.PaymentMethod.Should().Be("Cash");
        slice.Provider.Should().BeNull();
        slice.TotalCents.Should().Be(100000);
        slice.TransactionCount.Should().Be(2);
    }

    [Fact]
    public async Task OrderPaidWithoutPaymentRows_AbsentFromDoughnut_ButPresentInLine()
    {
        // Reproduces the reported root cause: an order flagged IsPaid=true with NO
        // OrderPayment rows. It appears in the line/bar charts (Orders/OrderItems)
        // but the payment-method doughnut (OrderPayments) has nothing to aggregate.
        var branchId = await SeedBranchAsync();
        await SeedPaidOrderWithoutPaymentsAsync(branchId, 50000,
            new DateTime(2026, 6, 6, 18, 0, 0, DateTimeKind.Utc));

        using var scope = _factory.Services.CreateScope();
        var reports = scope.ServiceProvider.GetRequiredService<IReportService>();
        var charts = await reports.GetDashboardChartsAsync(
            branchId, new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 7), "day");

        charts.SalesOverTime.Should().NotBeEmpty("the order has Order rows, so the line chart sees it");
        charts.TopProducts.Should().NotBeEmpty("the order has OrderItem rows, so the bar chart sees it");
        charts.SalesByPaymentMethod.Should().BeEmpty(
            "no OrderPayment rows exist → the doughnut is empty. Root cause is missing payment data, not the query.");
    }

    #region Helpers

    private async Task SeedPaidOrderWithoutPaymentsAsync(int branchId, int totalCents, DateTime createdAtUtc)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Orders.Add(new Order
        {
            Id = Guid.NewGuid().ToString(),
            BranchId = branchId,
            OrderNumber = 99,
            TotalCents = totalCents,
            PaidCents = totalCents,
            ChangeCents = 0,
            IsPaid = true, // legacy: flagged paid without persisting OrderPayment rows
            CreatedAt = createdAtUtc,
            Items = new List<OrderItem>
            {
                new() { ProductName = "Uñas", Quantity = 1, UnitPriceCents = totalCents }
            }
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> SeedBranchAsync() => (await SeedBranchWithOwnerAsync()).BranchId;

    private async Task<(int BranchId, int UserId)> SeedBranchWithOwnerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Business
        {
            Name = $"Dash-{suffix}",
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            PlanTypeId = PlanTypeIds.Pro,
            CountryCode = "MX",
            DefaultTaxId = 0,
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
            Email = $"dash-{suffix}@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("DashPass123!"),
            RoleId = UserRoleIds.Owner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        return (branch.Id, owner.Id);
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

    private async Task SeedPaidCashOrderAsync(
        int branchId, int totalCents, int tenderedCents, DateTime createdAtUtc, int? sessionId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var order = new Order
        {
            Id = Guid.NewGuid().ToString(),
            BranchId = branchId,
            OrderNumber = 1,
            TotalCents = totalCents,
            PaidCents = tenderedCents,
            ChangeCents = Math.Max(0, tenderedCents - totalCents),
            IsPaid = tenderedCents >= totalCents,
            CashRegisterSessionId = sessionId,
            CreatedAt = createdAtUtc,
            Payments = new List<OrderPayment>
            {
                new()
                {
                    Method = PaymentMethod.Cash,
                    MethodCode = "Cash",
                    Category = PaymentCategory.Cash,
                    SatPaymentFormCode = "01",
                    AmountCents = tenderedCents,
                    PaymentStatusId = PaymentStatus.Completed,
                    CreatedAt = createdAtUtc
                }
            }
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
    }

    #endregion
}
