using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Pos;

/// <summary>
/// Integration coverage for <c>POST /api/Pos/initialize-cashier-session</c>.
/// Verifies the five happy-path outcomes (Created / LinkedOrphan /
/// Reassigned / Idempotent / ForceTakeover), the 409 conflict requiring
/// operator confirmation, and three edge cases (inactive register,
/// cross-tenant branch override, invalid uuid validation).
/// </summary>
public class InitializeCashierSessionTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string Route = "/api/Pos/initialize-cashier-session";

    private readonly CustomWebApplicationFactory _factory;

    public InitializeCashierSessionTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Test 1 — Fresh tenant: device + register both created

    [Fact]
    public async Task IT_POS_1_FreshTenant_RegistersDevice_And_Creates_Register()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();

        var response = await client.PostAsJsonAsync(Route, new
        {
            deviceUuid = NewUuid(),
            registerName = "Caja Principal"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("outcome").GetString()
            .Should().Be("Created");
        body.RootElement.GetProperty("device").GetProperty("mode").GetString()
            .Should().Be("cashier");
        body.RootElement.GetProperty("register").GetProperty("name").GetString()
            .Should().Be("caja principal", "Name is normalized lowercase via the unique index helper");
        body.RootElement.TryGetProperty("closedSessionId", out _).Should().BeFalse(
            "closedSessionId is omitted under WhenWritingNull when the outcome is not ForceTakeover");

        // Cross-check persistence — the device + register exist and are linked.
        await AssertRegisterDeviceLinkedAsync(ctx.BranchId, ctx.OwnerUserId);
    }

    #endregion

    #region Test 2 — Orphan register (no device) → LinkedOrphan

    [Fact]
    public async Task IT_POS_2_OrphanRegister_Links_Without_Dialog()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();

        // Seed an orphan register without DeviceId — same shape as the
        // prod orphan id=18 that motivated this feature.
        await InsertOrphanRegisterAsync(ctx.BranchId, "caja principal");

        var response = await client.PostAsJsonAsync(Route, new
        {
            deviceUuid = NewUuid(),
            registerName = "Caja Principal"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("outcome").GetString()
            .Should().Be("LinkedOrphan");
        body.RootElement.GetProperty("register").GetProperty("deviceId").GetInt32()
            .Should().BeGreaterThan(0);
    }

    #endregion

    #region Test 3 — Same device repeat → Idempotent

    [Fact]
    public async Task IT_POS_3_SameDevice_Returns_Idempotent()
    {
        var (client, _) = await CreateAuthorizedClientForFreshOwnerAsync();
        var uuid = NewUuid();

        var first = await client.PostAsJsonAsync(Route, new { deviceUuid = uuid });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.PostAsJsonAsync(Route, new { deviceUuid = uuid });
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadJsonAsync(second);
        body.RootElement.GetProperty("outcome").GetString()
            .Should().Be("Idempotent");
    }

    #endregion

    #region Test 4 — Different device, no open session → silent Reassigned

    [Fact]
    public async Task IT_POS_4_DifferentDeviceNoOpenSession_Silent_Reassign()
    {
        var (client, _) = await CreateAuthorizedClientForFreshOwnerAsync();

        var firstUuid = NewUuid();
        var first = await client.PostAsJsonAsync(Route, new { deviceUuid = firstUuid });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondUuid = NewUuid();
        var second = await client.PostAsJsonAsync(Route, new { deviceUuid = secondUuid });
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await ReadJsonAsync(second);
        body.RootElement.GetProperty("outcome").GetString()
            .Should().Be("Reassigned");
    }

    #endregion

    #region Test 5 — Different device WITH open session, no Force → 409

    [Fact]
    public async Task IT_POS_5_DifferentDeviceOpenSession_Returns_409()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();

        var firstUuid = NewUuid();
        var first = await client.PostAsJsonAsync(Route, new { deviceUuid = firstUuid });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await ReadJsonAsync(first);
        var registerId = firstBody.RootElement.GetProperty("register").GetProperty("id").GetInt32();

        await InsertOpenSessionAsync(ctx.BranchId, registerId, ctx.OwnerUserId);

        var secondUuid = NewUuid();
        var conflict = await client.PostAsJsonAsync(Route, new { deviceUuid = secondUuid });

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var conflictBody = await ReadJsonAsync(conflict);
        conflictBody.RootElement.GetProperty("error").GetString()
            .Should().Be("session_open_on_other_device");
        conflictBody.RootElement.GetProperty("existingRegisterId").GetInt32()
            .Should().Be(registerId);
        conflictBody.RootElement.GetProperty("openSessionId").GetInt32()
            .Should().BeGreaterThan(0);
    }

    #endregion

    #region Test 6 — Force takeover closes the previous session and reassigns

    [Fact]
    public async Task IT_POS_6_Force_Takeover_Closes_Prev_Session_And_Reassigns()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();

        var firstUuid = NewUuid();
        var first = await client.PostAsJsonAsync(Route, new { deviceUuid = firstUuid });
        var firstBody = await ReadJsonAsync(first);
        var registerId = firstBody.RootElement.GetProperty("register").GetProperty("id").GetInt32();

        var openSessionId = await InsertOpenSessionAsync(ctx.BranchId, registerId, ctx.OwnerUserId);

        var secondUuid = NewUuid();
        var response = await client.PostAsJsonAsync(Route, new
        {
            deviceUuid = secondUuid,
            force = true
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await ReadJsonAsync(response);
        body.RootElement.GetProperty("outcome").GetString()
            .Should().Be("ForceTakeover");
        body.RootElement.GetProperty("closedSessionId").GetInt32()
            .Should().Be(openSessionId);

        // The previous session is closed and marked with the audit prefix
        // so cuadre reports can exclude it from financial totals.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = await db.CashRegisterSessions
            .IgnoreQueryFilters()
            .FirstAsync(s => s.Id == openSessionId);

        session.CashRegisterStatusId.Should().Be(CashRegisterStatus.Closed);
        session.Notes.Should().StartWith("FORCE_TAKEOVER:");
        session.CountedAmountCents.Should().BeNull(
            "force-close intentionally leaves balance columns at their pre-close defaults so reports can filter via the Notes prefix");
    }

    #endregion

    #region Test 7 — Inactive register cannot be revived through this endpoint

    [Fact]
    public async Task IT_POS_7_InactiveRegister_Returns_400()
    {
        var (client, ctx) = await CreateAuthorizedClientForFreshOwnerAsync();
        await InsertInactiveRegisterAsync(ctx.BranchId, "caja principal");

        var response = await client.PostAsJsonAsync(Route, new
        {
            deviceUuid = NewUuid(),
            registerName = "Caja Principal"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("desactivada",
            "an admin-suspended register must not be silently revived through the initialize flow");
    }

    #endregion

    #region Test 8 — Cross-tenant branch override returns 404 (no leak)

    [Fact]
    public async Task IT_POS_8_CrossTenant_BranchOverride_Returns_404()
    {
        var (client, _) = await CreateAuthorizedClientForFreshOwnerAsync();

        // Another tenant's branch via a direct seed.
        int otherTenantBranchId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var biz = new Domain.Models.Business
            {
                Name = $"OtherTenant-{Guid.NewGuid():N}",
                PrimaryMacroCategoryId = MacroCategoryIds.Services,
                PlanTypeId = PlanTypeIds.Free,
                CountryCode = "MX",
                DefaultTaxId = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Businesses.Add(biz);
            await db.SaveChangesAsync();

            var branch = new Domain.Models.Branch
            {
                BusinessId = biz.Id,
                Name = "Matrix",
                IsMatrix = true,
                IsActive = true,
                FolioCounter = 0,
                TimeZoneId = "America/Mexico_City",
                CreatedAt = DateTime.UtcNow
            };
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
            otherTenantBranchId = branch.Id;
        }

        var response = await client.PostAsJsonAsync(Route, new
        {
            deviceUuid = NewUuid(),
            branchIdOverride = otherTenantBranchId
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "cross-tenant branch overrides surface the same 404 as a missing branch to avoid leaking existence");
    }

    #endregion

    #region Test 9 — DeviceUuid validation (empty + over-length)

    [Fact]
    public async Task IT_POS_9_InvalidDeviceUuid_Returns_400()
    {
        var (client, _) = await CreateAuthorizedClientForFreshOwnerAsync();

        var emptyResponse = await client.PostAsJsonAsync(Route, new { deviceUuid = "" });
        emptyResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var tooLong = new string('x', 101);
        var longResponse = await client.PostAsJsonAsync(Route, new { deviceUuid = tooLong });
        longResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Helpers

    private record TenantCtx(int BusinessId, int BranchId, int OwnerUserId);

    /// <summary>
    /// Seeds a fresh tenant (Business + matrix Branch + Owner User) and
    /// returns an HttpClient bearing the Owner's JWT so each test starts
    /// from a known-clean state independent of the seeded Business 1.
    /// </summary>
    private async Task<(HttpClient Client, TenantCtx Ctx)> CreateAuthorizedClientForFreshOwnerAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var biz = new Domain.Models.Business
        {
            Name = $"PosTest-{suffix}",
            PrimaryMacroCategoryId = MacroCategoryIds.Services,
            // Enterprise — unlimited cashier-mode device quota so tests 4-6
            // (different devices on the same business) do not trip the
            // plan-limit gate that fires legitimately on smaller plans.
            PlanTypeId = PlanTypeIds.Enterprise,
            CountryCode = "MX",
            DefaultTaxId = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Businesses.Add(biz);
        await db.SaveChangesAsync();

        var branch = new Domain.Models.Branch
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

        var owner = new Domain.Models.User
        {
            BusinessId = biz.Id,
            BranchId = branch.Id,
            Name = $"Owner-{suffix}",
            Email = $"pos-{suffix}@example.com",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("PosPass123!"),
            RoleId = UserRoleIds.Owner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(owner);
        await db.SaveChangesAsync();

        var client = _factory.CreateClient();
        var token = JwtTestFactory.CreateUserToken(
            businessId: biz.Id,
            branchId: branch.Id,
            userId: owner.Id,
            role: "Owner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return (client, new TenantCtx(biz.Id, branch.Id, owner.Id));
    }

    private async Task InsertOrphanRegisterAsync(int branchId, string normalizedName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.CashRegisters.Add(new Domain.Models.CashRegister
        {
            BranchId = branchId,
            Name = normalizedName,
            DeviceId = null,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task InsertInactiveRegisterAsync(int branchId, string normalizedName)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.CashRegisters.Add(new Domain.Models.CashRegister
        {
            BranchId = branchId,
            Name = normalizedName,
            DeviceId = null,
            IsActive = false,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private async Task<int> InsertOpenSessionAsync(int branchId, int registerId, int openedByUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var session = new Domain.Models.CashRegisterSession
        {
            BranchId = branchId,
            CashRegisterId = registerId,
            OpenedByUserId = openedByUserId,
            OpenedAt = DateTime.UtcNow,
            InitialAmountCents = 0,
            CashRegisterStatusId = CashRegisterStatus.Open
        };
        db.CashRegisterSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }

    private async Task AssertRegisterDeviceLinkedAsync(int branchId, int ownerUserId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var register = await db.CashRegisters
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.BranchId == branchId && r.Name == "caja principal");
        register.Should().NotBeNull();
        register!.DeviceId.Should().NotBeNull("device.id must be assigned in the same transaction");
    }

    private static string NewUuid() => Guid.NewGuid().ToString();

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync());

    #endregion
}
