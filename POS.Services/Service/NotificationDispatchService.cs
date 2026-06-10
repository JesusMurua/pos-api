using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;
using POS.Services.Notifications;

namespace POS.Services.Service;

/// <inheritdoc />
public class NotificationDispatchService : INotificationDispatchService
{
    private readonly ApplicationDbContext _context;
    private readonly IEmailService _email;
    private readonly INotificationTemplateRegistry _registry;
    private readonly ILogger<NotificationDispatchService> _logger;

    private const int BatchSize = 50;
    private const int MaxAttempts = 6;

    // Backoff between retries, indexed by Attempts-1 (clamped). After MaxAttempts → Failed.
    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(6),
        TimeSpan.FromHours(24),
    };

    public NotificationDispatchService(
        ApplicationDbContext context,
        IEmailService email,
        INotificationTemplateRegistry registry,
        ILogger<NotificationDispatchService> logger)
    {
        _context = context;
        _email = email;
        _registry = registry;
        _logger = logger;
    }

    public async Task<int> DispatchPendingAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var batch = await _context.NotificationOutbox
            .Where(n => n.Status == NotificationStatus.Pending && n.NextAttemptAtUtc <= now)
            .OrderBy(n => n.NextAttemptAtUtc)
            .Take(BatchSize)
            .ToListAsync(ct);

        foreach (var row in batch)
        {
            ct.ThrowIfCancellationRequested();

            RenderedEmail rendered;
            try
            {
                var template = _registry.Get(row.TemplateCode);
                var payload = JsonSerializer.Deserialize<Dictionary<string, string>>(row.PayloadJson)
                              ?? new Dictionary<string, string>();
                rendered = template.Render(payload);
            }
            catch (Exception ex)
            {
                // A render/template/payload error is permanent — retrying won't fix a bad payload.
                MarkFailed(row, $"Render error: {Truncate(ex.Message)}");
                continue;
            }

            var result = await _email.SendNowAsync(row.ToEmail, rendered.Subject, rendered.BodyHtml, rendered.BodyText);
            switch (result)
            {
                case EmailSendResult.Sent:
                    row.Status = NotificationStatus.Sent;
                    row.SentAtUtc = DateTime.UtcNow;
                    row.LastError = null;
                    break;

                case EmailSendResult.PermanentError:
                    MarkFailed(row, "Permanent send error (non-retryable)");
                    break;

                case EmailSendResult.TransientError:
                    row.Attempts++;
                    if (row.Attempts >= MaxAttempts)
                    {
                        MarkFailed(row, $"Retry cap ({MaxAttempts}) reached");
                    }
                    else
                    {
                        row.NextAttemptAtUtc = DateTime.UtcNow + Backoff[Math.Min(row.Attempts - 1, Backoff.Length - 1)];
                        row.LastError = "Transient error — will retry";
                    }
                    break;
            }
        }

        if (batch.Count > 0) await _context.SaveChangesAsync(ct);
        return batch.Count;
    }

    private static void MarkFailed(NotificationOutbox row, string error)
    {
        row.Status = NotificationStatus.Failed;
        row.LastError = Truncate(error);
    }

    private static string Truncate(string s) => s.Length > 500 ? s[..500] : s;
}
