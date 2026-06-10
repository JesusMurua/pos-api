using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Helpers;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;
using POS.Services.IService;

namespace POS.IntegrationTests.Notifications;

/// <summary>
/// PR-5 daily trial reminders (EnqueueDueTrialNotificationsAsync): enqueues 3d/1d/expired for
/// ACTIVE businesses, deduped per (business, template, trial date), and SKIPS suspended tenants.
/// </summary>
public class TrialNotificationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public TrialNotificationTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task EnqueuesRemindersForDueWindows_AndSkipsSuspended()
    {
        var today = DateTime.UtcNow.Date;
        var in3d = await CreateBusinessWithTrialAsync(today.AddDays(3), active: true);
        var in1d = await CreateBusinessWithTrialAsync(today.AddDays(1), active: true);
        var expired = await CreateBusinessWithTrialAsync(today, active: true);
        var suspended = await CreateBusinessWithTrialAsync(today.AddDays(3), active: false);

        await RunAsync();

        (await HasAsync(in3d, "TrialExpiring3d")).Should().BeTrue();
        (await HasAsync(in1d, "TrialExpiring1d")).Should().BeTrue();
        (await HasAsync(expired, "TrialExpired")).Should().BeTrue();
        (await AnyTrialAsync(suspended)).Should().BeFalse("suspended tenants get no dunning/trial mail");
    }

    [Fact]
    public async Task IsIdempotent_AcrossReRuns()
    {
        var today = DateTime.UtcNow.Date;
        var biz = await CreateBusinessWithTrialAsync(today.AddDays(3), active: true);

        await RunAsync();
        await RunAsync(); // second run same day must not duplicate

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.NotificationOutbox.CountAsync(n => n.BusinessId == biz && n.TemplateCode == "TrialExpiring3d"))
            .Should().Be(1);
    }

    #region Helpers

    private async Task RunAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<INotificationService>();
        await svc.EnqueueDueTrialNotificationsAsync();
    }

    private async Task<bool> HasAsync(int businessId, string code)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.NotificationOutbox.AnyAsync(n => n.BusinessId == businessId && n.TemplateCode == code);
    }

    private async Task<bool> AnyTrialAsync(int businessId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.NotificationOutbox.AnyAsync(n => n.BusinessId == businessId && n.TemplateCode.StartsWith("Trial"));
    }

    private async Task<int> CreateBusinessWithTrialAsync(DateTime trialEndsAt, bool active)
    {
        var businessId = await CreateBusinessAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var biz = await db.Businesses.IgnoreQueryFilters().FirstAsync(b => b.Id == businessId);
        biz.TrialEndsAt = trialEndsAt;
        biz.IsActive = active;
        await db.SaveChangesAsync();
        return businessId;
    }

    private async Task<int> CreateBusinessAsync()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Admin-Token", TestConstants.AdminApiToken);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var resp = await client.PostAsJsonAsync("/api/Admin/businesses", new
        {
            businessName = $"Trial-{suffix}",
            ownerName = $"Owner {suffix}",
            email = $"trial-{suffix}@example.com",
            password = "TrialPass123!",
            primaryMacroCategoryId = MacroCategoryIds.Services,
            planTypeId = PlanTypeIds.Basic,
            countryCode = "MX"
        });
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("businessId").GetInt32();
    }

    #endregion
}
