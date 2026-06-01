using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.DTOs.Admin;
using POS.Domain.Helpers;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;

namespace POS.IntegrationTests.Admin;

/// <summary>
/// Coverage for the admin operational endpoints added in BDD admin-ops:
/// detail view, status toggle (with login-gate verification), plan
/// change, password reset, trial extension, impersonation, and the
/// filter/sort expansion on the directory list.
/// </summary>
public class AdminBusinessOperationsTests : IClassFixture<CustomWebApplicationFactory>
{
    private const string AdminRoute = "/api/Admin/businesses";
    private const string AdminTokenHeader = "X-Admin-Token";

    private readonly CustomWebApplicationFactory _factory;

    public AdminBusinessOperationsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    #region Detail — GetById happy path + 404

    [Fact]
    public async Task IT_ADM_OPS_1_GetById_Returns_Detail_For_Existing_Business()
    {
        var client = CreateAdminClient();
        var businessId = await CreateBusinessViaAdminAsync(client);

        var response = await client.GetAsync($"{AdminRoute}/{businessId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("id").GetInt32().Should().Be(businessId);
        document.RootElement.GetProperty("planTypeCode").GetString().Should().Be("Basic");
        document.RootElement.GetProperty("primaryMacroCategoryCode").GetString().Should().Be("services");
        document.RootElement.TryGetProperty("snapshot", out _).Should().BeTrue();
        document.RootElement.TryGetProperty("branches", out _).Should().BeTrue();
    }

    [Fact]
    public async Task IT_ADM_OPS_2_GetById_Returns_404_For_Unknown_Id()
    {
        var client = CreateAdminClient();

        var response = await client.GetAsync($"{AdminRoute}/99999999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Status toggle — suspending blocks login + reactivation restores it

    [Fact]
    public async Task IT_ADM_OPS_3_ToggleStatus_Suspend_Blocks_Owner_Login()
    {
        var client = CreateAdminClient();
        var (businessId, email, password) = await CreateBusinessWithKnownOwnerAsync(client);

        // Sanity: an active tenant authenticates.
        var preLogin = await PostJsonAsync("/api/Auth/email-login", new { email, password });
        preLogin.StatusCode.Should().Be(HttpStatusCode.OK);

        // Suspend.
        var suspend = await client.PatchAsJsonAsync(
            $"{AdminRoute}/{businessId}/status",
            new AdminToggleBusinessStatusRequest { IsActive = false, Reason = "Test" });
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        // Suspended tenant cannot login anymore (gate from commit 1).
        var postLogin = await PostJsonAsync("/api/Auth/email-login", new { email, password });
        postLogin.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IT_ADM_OPS_4_ToggleStatus_Reactivate_Restores_Detail_IsActive_True()
    {
        var client = CreateAdminClient();
        var businessId = await CreateBusinessViaAdminAsync(client);

        var suspend = await client.PatchAsJsonAsync(
            $"{AdminRoute}/{businessId}/status",
            new AdminToggleBusinessStatusRequest { IsActive = false });
        suspend.StatusCode.Should().Be(HttpStatusCode.OK);

        var reactivate = await client.PatchAsJsonAsync(
            $"{AdminRoute}/{businessId}/status",
            new AdminToggleBusinessStatusRequest { IsActive = true });
        reactivate.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await reactivate.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("isActive").GetBoolean().Should().BeTrue();
    }

    #endregion

    #region Change plan — persisted + feature cache invalidated

    [Fact]
    public async Task IT_ADM_OPS_5_ChangePlan_Persists_New_PlanTypeId()
    {
        var client = CreateAdminClient();
        var businessId = await CreateBusinessViaAdminAsync(client);

        var change = await client.PatchAsJsonAsync(
            $"{AdminRoute}/{businessId}/plan",
            new AdminChangePlanRequest { PlanTypeId = PlanTypeIds.Pro });
        change.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await change.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("planTypeId").GetInt32().Should().Be(PlanTypeIds.Pro);
        document.RootElement.GetProperty("planTypeCode").GetString().Should().Be("Pro");

        // Verify persistence via direct read.
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var biz = await db.Businesses
            .IgnoreQueryFilters()
            .FirstAsync(b => b.Id == businessId);
        biz.PlanTypeId.Should().Be(PlanTypeIds.Pro);
    }

    #endregion

    #region Reset owner password — generated + provided

    [Fact]
    public async Task IT_ADM_OPS_6_ResetOwnerPassword_Generates_When_Null()
    {
        var client = CreateAdminClient();
        var businessId = await CreateBusinessViaAdminAsync(client);

        var response = await client.PostAsJsonAsync(
            $"{AdminRoute}/{businessId}/reset-owner-password",
            new AdminResetOwnerPasswordRequest());
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var newPassword = document.RootElement.GetProperty("newPassword").GetString();
        newPassword.Should().NotBeNullOrWhiteSpace();
        newPassword!.Length.Should().Be(12,
            "the generated password is 12 cryptographically-random chars");
    }

    [Fact]
    public async Task IT_ADM_OPS_7_ResetOwnerPassword_Uses_Provided_When_Specified()
    {
        var client = CreateAdminClient();
        var (businessId, email, _) = await CreateBusinessWithKnownOwnerAsync(client);

        var newPassword = "SpecificPass456!";
        var response = await client.PostAsJsonAsync(
            $"{AdminRoute}/{businessId}/reset-owner-password",
            new AdminResetOwnerPasswordRequest { NewPassword = newPassword });
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("newPassword").GetString().Should().Be(newPassword);

        // Verify the Owner can authenticate with the new password.
        var login = await PostJsonAsync("/api/Auth/email-login", new { email, password = newPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK,
            "the new password must be effective end-to-end");
    }

    #endregion

    #region Extend trial — validation

    [Fact]
    public async Task IT_ADM_OPS_8_ExtendTrial_Rejects_Past_Date()
    {
        var client = CreateAdminClient();
        var businessId = await CreateBusinessViaAdminAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"{AdminRoute}/{businessId}/trial",
            new AdminExtendTrialRequest { NewTrialEndsAt = DateTime.UtcNow.AddDays(-1) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task IT_ADM_OPS_9_ExtendTrial_Rejects_Beyond_180_Days()
    {
        var client = CreateAdminClient();
        var businessId = await CreateBusinessViaAdminAsync(client);

        var response = await client.PatchAsJsonAsync(
            $"{AdminRoute}/{businessId}/trial",
            new AdminExtendTrialRequest { NewTrialEndsAt = DateTime.UtcNow.AddDays(200) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Impersonate — short-TTL JWT + no-Owner 404

    [Fact]
    public async Task IT_ADM_OPS_10_Impersonate_Returns_Owner_Jwt_With_Short_Ttl()
    {
        var client = CreateAdminClient();
        var businessId = await CreateBusinessViaAdminAsync(client);

        var response = await client.PostAsync($"{AdminRoute}/{businessId}/impersonate", content: null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var jwt = document.RootElement.GetProperty("ownerJwt").GetString();
        jwt.Should().NotBeNullOrWhiteSpace();

        var parsed = new JwtSecurityTokenHandler().ReadJwtToken(jwt);
        // ValidFrom is unset in this codebase's GenerateToken (no nbf
        // claim), so measure TTL against the test-time clock instead.
        var ttl = parsed.ValidTo - DateTime.UtcNow;
        ttl.TotalHours.Should().BeApproximately(2.0, 0.1,
            "impersonation tokens are 2-hour TTL to cap blast radius");
    }

    [Fact]
    public async Task IT_ADM_OPS_11_Impersonate_Returns_404_When_Business_Has_No_Owner()
    {
        var client = CreateAdminClient();

        // Seed a Business directly with no Owner User.
        int businessId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var biz = new Domain.Models.Business
            {
                Name = $"NoOwner-{Guid.NewGuid():N}",
                PrimaryMacroCategoryId = MacroCategoryIds.Services,
                PlanTypeId = PlanTypeIds.Free,
                CountryCode = "MX",
                DefaultTaxId = 0,
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            db.Businesses.Add(biz);
            await db.SaveChangesAsync();
            businessId = biz.Id;
        }

        var response = await client.PostAsync($"{AdminRoute}/{businessId}/impersonate", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region List filters — by plan + by trialStatus

    [Fact]
    public async Task IT_ADM_OPS_12_List_Filters_By_PlanTypeId_And_TrialStatus()
    {
        var client = CreateAdminClient();

        // Create two distinct businesses with distinct plans to verify the
        // planTypeId filter discriminates correctly. Free plan is used
        // because it has no trial, so we expect the trialStatus=active
        // filter to exclude it.
        await CreateBusinessViaAdminAsync(client, planTypeId: PlanTypeIds.Free);
        await CreateBusinessViaAdminAsync(client, planTypeId: PlanTypeIds.Pro);

        // planTypeId filter narrows to Pro only.
        var byPlan = await client.GetAsync(
            $"{AdminRoute}?planTypeId={PlanTypeIds.Pro}&pageSize=100");
        byPlan.StatusCode.Should().Be(HttpStatusCode.OK);

        var byPlanBody = JsonDocument.Parse(await byPlan.Content.ReadAsStringAsync());
        var items = byPlanBody.RootElement.GetProperty("items").EnumerateArray().ToList();
        items.Should().NotBeEmpty();
        items.Select(i => i.GetProperty("planTypeId").GetInt32())
            .Should().AllSatisfy(p => p.Should().Be(PlanTypeIds.Pro));

        // trialStatus=active should include only businesses with an
        // active trial window (Pro plan auto-assigns +14 days).
        var byTrial = await client.GetAsync(
            $"{AdminRoute}?trialStatus=active&pageSize=100");
        byTrial.StatusCode.Should().Be(HttpStatusCode.OK);

        var byTrialBody = JsonDocument.Parse(await byTrial.Content.ReadAsStringAsync());
        var trialItems = byTrialBody.RootElement.GetProperty("items").EnumerateArray().ToList();
        trialItems.Should().AllSatisfy(i =>
        {
            i.TryGetProperty("trialEndsAt", out var trialProp).Should().BeTrue();
            trialProp.GetString().Should().NotBeNullOrEmpty();
        });
    }

    #endregion

    #region Helpers

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminTokenHeader, TestConstants.AdminApiToken);
        return client;
    }

    private static string NewUniqueSuffix() => Guid.NewGuid().ToString("N")[..12];

    /// <summary>
    /// Creates a Business via the public admin POST endpoint. Returns the
    /// new business id. Defaults: macro=Services, plan=Basic.
    /// </summary>
    private async Task<int> CreateBusinessViaAdminAsync(
        HttpClient client, int planTypeId = 2)
    {
        var suffix = NewUniqueSuffix();
        var request = new
        {
            businessName = $"OpsTest-{suffix}",
            ownerName = $"Owner {suffix}",
            email = $"ops-{suffix}@example.com",
            password = "OpsPass123!",
            primaryMacroCategoryId = MacroCategoryIds.Services,
            planTypeId,
            countryCode = "MX"
        };
        var response = await client.PostAsJsonAsync(AdminRoute, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("businessId").GetInt32();
    }

    /// <summary>
    /// Creates a Business and returns the credentials the admin created
    /// for its Owner so the test can exercise the login flow end-to-end.
    /// </summary>
    private async Task<(int BusinessId, string Email, string Password)> CreateBusinessWithKnownOwnerAsync(
        HttpClient client)
    {
        var suffix = NewUniqueSuffix();
        var email = $"ops-known-{suffix}@example.com";
        var password = "OpsKnown123!";
        var request = new
        {
            businessName = $"OpsKnown-{suffix}",
            ownerName = $"Owner {suffix}",
            email,
            password,
            primaryMacroCategoryId = MacroCategoryIds.Services,
            planTypeId = PlanTypeIds.Basic,
            countryCode = "MX"
        };
        var response = await client.PostAsJsonAsync(AdminRoute, request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var businessId = body.RootElement.GetProperty("businessId").GetInt32();
        return (businessId, email, password);
    }

    private async Task<HttpResponseMessage> PostJsonAsync(string url, object payload)
    {
        var client = _factory.CreateClient();
        return await client.PostAsJsonAsync(url, payload);
    }

    #endregion
}
