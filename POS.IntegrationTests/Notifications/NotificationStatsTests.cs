using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;
using POS.Services.IService;

namespace POS.IntegrationTests.Notifications;

/// <summary>
/// PR-6 notification health embedded in billing metrics. Windows by FailedAtUtc (the real failure
/// time, not creation) so a slow-failing row counts in the right window. Also locks the PR-5
/// follow-up: an unresolved-recipient enqueue produces a queryable Failed row. Isolated factory.
/// </summary>
public class NotificationStatsTests
{
    [Fact]
    public async Task FailedWindows_UseFailedAtUtc()
    {
        using var f = new CustomWebApplicationFactory();
        var now = DateTime.UtcNow;
        // Created days ago, but failed within the last hour → must count in 24h and 7d.
        await SeedAsync(f, NotificationStatus.Failed, createdAt: now.AddDays(-5), failedAt: now.AddHours(-1));
        // Failed 3 days ago → counts in 7d, not 24h.
        await SeedAsync(f, NotificationStatus.Failed, createdAt: now.AddDays(-10), failedAt: now.AddDays(-3));
        // Failed 10 days ago → total only.
        await SeedAsync(f, NotificationStatus.Failed, createdAt: now.AddDays(-20), failedAt: now.AddDays(-10));

        var stats = (await MetricsAsync(f)).NotificationStats;
        stats.Failed24h.Should().Be(1);
        stats.Failed7d.Should().Be(2);
        stats.FailedTotal.Should().Be(3);
    }

    [Fact]
    public async Task Pending_AndOldestAge()
    {
        using var f = new CustomWebApplicationFactory();
        await SeedAsync(f, NotificationStatus.Pending, createdAt: DateTime.UtcNow.AddMinutes(-90), failedAt: null);

        var stats = (await MetricsAsync(f)).NotificationStats;
        stats.Pending.Should().Be(1);
        stats.OldestPendingAgeMinutes.Should().BeGreaterThanOrEqualTo(89);
    }

    [Fact]
    public async Task UnresolvedRecipientEnqueue_ProducesFailedRow()
    {
        using var f = new CustomWebApplicationFactory();

        // A business with no owner User → Owner email cannot be resolved at enqueue time.
        int businessId;
        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var biz = new Business { Name = "NoOwner", CreatedAt = DateTime.UtcNow, IsActive = true };
            db.Businesses.Add(biz);
            await db.SaveChangesAsync();
            businessId = biz.Id;
        }

        using (var scope = f.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
            await notifications.EnqueueAsync("PlanChanged", NotificationRecipientType.Owner, businessId,
                new Dictionary<string, string> { ["oldPlan"] = "Basic", ["newPlan"] = "Pro" });
            await db.SaveChangesAsync(); // EnqueueAsync is no-save by design

            var row = await db.NotificationOutbox.FirstAsync(n => n.BusinessId == businessId);
            row.Status.Should().Be(NotificationStatus.Failed);
            row.ToEmail.Should().Be("(unresolved)");
            row.LastError.Should().Be("recipient unresolved");
            row.FailedAtUtc.Should().NotBeNull();
        }

        (await MetricsAsync(f)).NotificationStats.FailedTotal.Should().BeGreaterThanOrEqualTo(1);
    }

    #region Helpers

    private static async Task<Domain.DTOs.Admin.AdminBillingMetricsDto> MetricsAsync(CustomWebApplicationFactory f)
    {
        using var scope = f.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IBillingMetricsService>();
        return await svc.GetMetricsAsync();
    }

    private static async Task SeedAsync(
        CustomWebApplicationFactory f, NotificationStatus status, DateTime createdAt, DateTime? failedAt)
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.NotificationOutbox.Add(new NotificationOutbox
        {
            TemplateCode = "Reactivated",
            RecipientType = NotificationRecipientType.Custom,
            ToEmail = "x@example.com",
            PayloadJson = "{}",
            Status = status,
            Attempts = status == NotificationStatus.Failed ? 6 : 0,
            NextAttemptAtUtc = createdAt,
            CreatedAtUtc = createdAt,
            FailedAtUtc = failedAt
        });
        await db.SaveChangesAsync();
    }

    #endregion
}
