using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Tenancy;

/// <summary>
/// End-to-end proof that the EF Core global query filters introduced by the
/// tenant-isolation refactor stop cross-tenant data leakage at two layers:
///   1. HTTP — a request with Business A's JWT must not see Business B's data
///      via the controller pipeline.
///   2. DbContext — a raw <see cref="ApplicationDbContext"/>.<see cref="DbSet{TEntity}"/>
///      query (no manual <c>Where</c>) must already be tenant-scoped by the
///      <c>HasQueryFilter</c> machinery alone.
/// </summary>
public class TenantIsolationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    private const int BranchAOrderNumber = 1001;
    private const int BranchBOrderNumber = 2002;

    public TenantIsolationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        // Building the client triggers the host pipeline, which runs the
        // Program.cs startup block (with the IsRelational() guard) and seeds
        // the system catalogs via DbInitializer.SeedSystemDataAsync.
        _client = factory.CreateClient();

        SeedTenants();
    }

    [Fact]
    public async Task GetOrders_With_BusinessA_Token_Returns_Only_BusinessA_Orders()
    {
        var token = JwtTestFactory.CreateUserToken(
            businessId: TestConstants.BusinessAId,
            branchId: TestConstants.BranchAId,
            userId: TestConstants.UserAId,
            role: "Owner");

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var queryDate = TimeZoneHelper.GetLocalToday(TimeZoneHelper.DefaultTimeZone);
        var response = await _client.GetAsync(
            $"/api/Orders?date={queryDate:yyyy-MM-dd}");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "got {0} with body: {1}", response.StatusCode, body);

        // Inspect raw JSON: the production API serializes enums as strings
        // (JsonStringEnumConverter) and Order's full graph is not worth
        // deserializing here — only the BranchId per element is needed
        // to prove the tenant filter held.
        using var document = JsonDocument.Parse(body);
        var branchIds = document.RootElement
            .EnumerateArray()
            .Select(element => element.GetProperty("branchId").GetInt32())
            .ToList();

        branchIds.Should().Contain(TestConstants.BranchAId,
            "Business A's request must see its own branch's order");
        branchIds.Should().NotContain(TestConstants.BranchBId,
            "Business A's request must never see Business B's order");
    }

    [Fact]
    public async Task DbContext_Globally_Scopes_Orders_To_Current_Branch_Without_Manual_Where()
    {
        using var scope = _factory.Services.CreateScope();
        var options = scope.ServiceProvider
            .GetRequiredService<DbContextOptions<ApplicationDbContext>>();

        // Localized FakeTenantContext — no DI override, no shared mutable state.
        var fakeTenant = new FakeTenantContext
        {
            BusinessId = TestConstants.BusinessAId,
            BranchId = TestConstants.BranchAId
        };

        using var db = new ApplicationDbContext(options, fakeTenant);

        // NO .Where(o => o.BranchId == ...) — the global query filter is the
        // ONLY thing scoping the result set. If it were missing, both seeded
        // orders would land in the list.
        var orders = await db.Orders.ToListAsync();

        orders.Should().NotBeEmpty();
        orders.Should().OnlyContain(o => o.BranchId == TestConstants.BranchAId,
            "the global query filter must restrict results to the current branch " +
            "even when no explicit Where clause is applied");
    }

    /// <summary>
    /// Seeds Business B + Branch B and one order per branch. Business A
    /// (Id=1) and Branch A (Id=1) already exist via the
    /// <c>ApplicationDbContext.HasData</c> seeds applied by
    /// <c>Database.EnsureCreated()</c> on first DbSet access.
    /// </summary>
    private void SeedTenants()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // First DbSet access triggers EnsureCreated() under InMemory, applying
        // the HasData seeds (Business 1, Branch 1, Categories, Users, ...).
        // SeedSystemDataAsync from Program.cs has already run at host boot.
        if (db.Businesses.IgnoreQueryFilters().Any(b => b.Id == TestConstants.BusinessBId))
        {
            return; // Already seeded by a prior IClassFixture cycle.
        }

        db.Businesses.Add(new Business
        {
            Id = TestConstants.BusinessBId,
            Name = "Integration Test Business B",
            PrimaryMacroCategoryId = MacroCategoryIds.FoodBeverage,
            PlanTypeId = PlanTypeIds.Basic,
            DefaultTaxId = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });

        db.Branches.Add(new Branch
        {
            Id = TestConstants.BranchBId,
            BusinessId = TestConstants.BusinessBId,
            Name = "Integration Test Branch B",
            IsMatrix = true,
            IsActive = true,
            TimeZoneId = TimeZoneHelper.DefaultTimeZone,
            CreatedAt = DateTime.UtcNow
        });

        var nowUtc = DateTime.UtcNow;
        db.Orders.Add(new Order
        {
            Id = Guid.NewGuid().ToString(),
            BranchId = TestConstants.BranchAId,
            OrderNumber = BranchAOrderNumber,
            CreatedAt = nowUtc
        });
        db.Orders.Add(new Order
        {
            Id = Guid.NewGuid().ToString(),
            BranchId = TestConstants.BranchBId,
            OrderNumber = BranchBOrderNumber,
            CreatedAt = nowUtc
        });

        db.SaveChanges();
    }
}
