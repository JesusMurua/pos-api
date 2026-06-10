using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.IntegrationTests.Infrastructure;
using POS.Repository;
using POS.Services.IService;

namespace POS.IntegrationTests.Notifications;

/// <summary>
/// PR-5 dispatch service: status transitions + backoff driven by the (faked) email send result.
/// Uses the factory's IEmailService mock to simulate Sent / TransientError / PermanentError, and
/// a malformed payload to exercise the render-failure path. Each test asserts on its own seeded
/// row by Id, so dispatching other pending rows in the shared DB is harmless.
/// </summary>
public class NotificationDispatchTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public NotificationDispatchTests(CustomWebApplicationFactory factory) => _factory = factory;

    [Fact]
    public async Task Sent_MarksSentWithTimestamp()
    {
        SetSendResult(EmailSendResult.Sent);
        var id = await SeedAsync("Reactivated", "{}", attempts: 0);

        await DispatchAsync();

        var row = await GetAsync(id);
        row.Status.Should().Be(NotificationStatus.Sent);
        row.SentAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task TransientError_SchedulesRetry_BelowCap()
    {
        SetSendResult(EmailSendResult.TransientError);
        var id = await SeedAsync("Reactivated", "{}", attempts: 0);
        var before = DateTime.UtcNow;

        await DispatchAsync();

        var row = await GetAsync(id);
        row.Status.Should().Be(NotificationStatus.Pending, "still retryable");
        row.Attempts.Should().Be(1);
        row.NextAttemptAtUtc.Should().BeAfter(before, "backoff pushes the next attempt out");
    }

    [Fact]
    public async Task TransientError_AtCap_MarksFailed()
    {
        SetSendResult(EmailSendResult.TransientError);
        var id = await SeedAsync("Reactivated", "{}", attempts: 5); // 6th attempt hits the cap

        await DispatchAsync();

        var row = await GetAsync(id);
        row.Status.Should().Be(NotificationStatus.Failed);
        row.Attempts.Should().Be(6);
    }

    [Fact]
    public async Task PermanentError_MarksFailedImmediately()
    {
        SetSendResult(EmailSendResult.PermanentError);
        var id = await SeedAsync("Reactivated", "{}", attempts: 0);

        await DispatchAsync();

        (await GetAsync(id)).Status.Should().Be(NotificationStatus.Failed);
    }

    [Fact]
    public async Task RenderFailure_MarksFailed_WithoutSending()
    {
        SetSendResult(EmailSendResult.Sent); // would succeed if it ever reached send
        // InvoiceCreated requires invoiceNumber/totalPesos/dueDate — an empty payload throws on render.
        var id = await SeedAsync("InvoiceCreated", "{}", attempts: 0);

        await DispatchAsync();

        var row = await GetAsync(id);
        row.Status.Should().Be(NotificationStatus.Failed);
        row.LastError.Should().Contain("Render error");
    }

    #region Helpers

    private void SetSendResult(EmailSendResult result)
    {
        _factory.EmailServiceMock.Reset();
        _factory.EmailServiceMock
            .Setup(x => x.SendNowAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(result);
    }

    private async Task DispatchAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var dispatch = scope.ServiceProvider.GetRequiredService<INotificationDispatchService>();
        await dispatch.DispatchPendingAsync();
    }

    private async Task<int> SeedAsync(string templateCode, string payloadJson, int attempts)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var row = new NotificationOutbox
        {
            BusinessId = null,
            TemplateCode = templateCode,
            RecipientType = NotificationRecipientType.Custom,
            ToEmail = "test@example.com",
            PayloadJson = payloadJson,
            Status = NotificationStatus.Pending,
            Attempts = attempts,
            NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(-1),
            CreatedAtUtc = DateTime.UtcNow
        };
        db.NotificationOutbox.Add(row);
        await db.SaveChangesAsync();
        return row.Id;
    }

    private async Task<NotificationOutbox> GetAsync(int id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.NotificationOutbox.AsNoTracking().FirstAsync(n => n.Id == id);
    }

    #endregion
}
