using POS.Domain.Enums;

namespace POS.Services.IService;

/// <summary>
/// Enqueues transactional emails into the durable NotificationOutbox (PR-5). The actual
/// send is done later by the dispatch worker with retries/backoff.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Resolves the recipient email AT ENQUEUE TIME and adds an outbox row WITHOUT calling
    /// SaveChanges (the caller's transaction flushes it — same pattern as
    /// BusinessAuditService.Record). Best-effort by design: it NEVER throws, so a notification
    /// failure can never abort the business operation that triggered it (same discipline as
    /// SafeRecordAuditAsync). Skips when a dedupKey already exists.
    /// </summary>
    Task EnqueueAsync(
        string templateCode,
        NotificationRecipientType recipientType,
        int? businessId,
        IReadOnlyDictionary<string, string> payload,
        string? customEmail = null,
        string? dedupKey = null);

    /// <summary>
    /// Manual super-admin send (POST /Admin/businesses/{id}/notify): enqueues for immediate
    /// dispatch AND writes a BusinessAuditLog(NotificationSent) — the only path that audits a
    /// notification (event-driven sends are traced by the outbox row itself). Owns its SaveChanges.
    /// </summary>
    Task EnqueueManualAsync(int businessId, string templateCode, IReadOnlyDictionary<string, string> payload, string? tokenId);

    /// <summary>
    /// Daily job: enqueues trial-expiry reminders (3d/1d/expired) for ACTIVE businesses, deduped
    /// per (business, template, trial date). Suspended businesses are skipped. Owns its SaveChanges.
    /// </summary>
    Task<int> EnqueueDueTrialNotificationsAsync(CancellationToken ct = default);
}
