using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class NotificationService : INotificationService
{
    private readonly ApplicationDbContext _context;
    private readonly IBusinessAuditService _audit;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        ApplicationDbContext context,
        IBusinessAuditService audit,
        ILogger<NotificationService> logger)
    {
        _context = context;
        _audit = audit;
        _logger = logger;
    }

    public async Task EnqueueAsync(
        string templateCode,
        NotificationRecipientType recipientType,
        int? businessId,
        IReadOnlyDictionary<string, string> payload,
        string? customEmail = null,
        string? dedupKey = null)
    {
        // Best-effort by design: a notification must NEVER abort the business operation that
        // triggered it (a lost email is acceptable; a failed payment/suspend is not). Same
        // discipline as SafeRecordAuditAsync. DO NOT let this method throw.
        try
        {
            if (dedupKey != null &&
                await _context.NotificationOutbox.AnyAsync(n => n.DedupKey == dedupKey))
                return;

            var toEmail = await ResolveEmailAsync(recipientType, businessId, customEmail);
            if (string.IsNullOrWhiteSpace(toEmail))
            {
                // Record an unresolved-recipient failure as a queryable Failed row (not just a log)
                // so the metrics endpoint can surface this class of silent enqueue failure (PR-6).
                _logger.LogError("Notification '{Template}' unresolved recipient for business {BusinessId}",
                    templateCode, businessId);
                var failed = BuildRow(templateCode, recipientType, businessId, "(unresolved)", payload, dedupKey);
                failed.Status = NotificationStatus.Failed;
                failed.LastError = "recipient unresolved";
                failed.FailedAtUtc = DateTime.UtcNow;
                _context.NotificationOutbox.Add(failed);
                return;
            }

            _context.NotificationOutbox.Add(BuildRow(templateCode, recipientType, businessId, toEmail, payload, dedupKey));
            // No SaveChanges — the caller's transaction flushes this row.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enqueue notification '{Template}' for business {BusinessId}",
                templateCode, businessId);
        }
    }

    public async Task EnqueueManualAsync(
        int businessId, string templateCode, IReadOnlyDictionary<string, string> payload, string? tokenId)
    {
        var toEmail = await ResolveEmailAsync(NotificationRecipientType.Owner, businessId, null)
            ?? throw new POS.Domain.Exceptions.NotFoundException(
                $"Business {businessId} has no resolvable recipient email.");

        _context.NotificationOutbox.Add(
            BuildRow(templateCode, NotificationRecipientType.Owner, businessId, toEmail, payload, dedupKey: null));

        // Manual sends are the ONLY notification path that writes BusinessAuditLog (event-driven
        // sends are traced by the outbox row). Avoids inflating the audit log with bulk notifs.
        _audit.Record(BusinessAuditAction.NotificationSent, businessId, null,
            before: null, after: new { templateCode }, tokenId);

        await _context.SaveChangesAsync();
    }

    public async Task<int> EnqueueDueTrialNotificationsAsync(CancellationToken ct = default)
    {
        var today = DateTime.UtcNow.Date;

        // Active businesses on a trial. Suspended (IsActive=false) tenants are skipped — no dunning.
        var candidates = await _context.Businesses.IgnoreQueryFilters()
            .Where(b => b.IsActive && b.TrialEndsAt != null)
            .Select(b => new { b.Id, b.PlanTypeId, b.TrialEndsAt })
            .ToListAsync(ct);

        var enqueued = 0;
        foreach (var b in candidates)
        {
            var daysLeft = (b.TrialEndsAt!.Value.Date - today).Days;
            var code = daysLeft switch
            {
                3 => "TrialExpiring3d",
                1 => "TrialExpiring1d",
                0 => "TrialExpired",
                _ => null
            };
            if (code == null) continue;

            var dedupKey = $"{code}:{b.Id}:{b.TrialEndsAt.Value:yyyyMMdd}";
            if (await _context.NotificationOutbox.AnyAsync(n => n.DedupKey == dedupKey, ct)) continue;

            var toEmail = await ResolveEmailAsync(NotificationRecipientType.Owner, b.Id, null);
            if (string.IsNullOrWhiteSpace(toEmail)) continue;

            var payload = new Dictionary<string, string> { ["plan"] = PlanTypeIds.ToCode(b.PlanTypeId) };
            _context.NotificationOutbox.Add(
                BuildRow(code, NotificationRecipientType.Owner, b.Id, toEmail, payload, dedupKey));
            enqueued++;
        }

        if (enqueued > 0) await _context.SaveChangesAsync(ct);
        return enqueued;
    }

    #region Helpers

    private NotificationOutbox BuildRow(
        string templateCode, NotificationRecipientType recipientType, int? businessId,
        string toEmail, IReadOnlyDictionary<string, string> payload, string? dedupKey) => new()
    {
        BusinessId = businessId,
        TemplateCode = templateCode,
        RecipientType = recipientType,
        ToEmail = toEmail,
        PayloadJson = JsonSerializer.Serialize(payload),
        Status = NotificationStatus.Pending,
        Attempts = 0,
        NextAttemptAtUtc = DateTime.UtcNow,
        DedupKey = dedupKey,
        CreatedAtUtc = DateTime.UtcNow
    };

    private async Task<string?> ResolveEmailAsync(
        NotificationRecipientType recipientType, int? businessId, string? customEmail)
    {
        if (recipientType == NotificationRecipientType.Custom)
            return customEmail;

        if (businessId == null) return null;

        if (recipientType == NotificationRecipientType.BillingEmail)
        {
            var billing = await _context.Subscriptions.IgnoreQueryFilters()
                .Where(s => s.BusinessId == businessId)
                .Select(s => s.BillingEmail)
                .FirstOrDefaultAsync();
            if (!string.IsNullOrWhiteSpace(billing)) return billing;
            // fall through to the owner email
        }

        return await _context.Users.IgnoreQueryFilters()
            .Where(u => u.BusinessId == businessId && u.RoleId == UserRoleIds.Owner && u.Email != null)
            .OrderBy(u => u.CreatedAt)
            .Select(u => u.Email)
            .FirstOrDefaultAsync();
    }

    #endregion
}
