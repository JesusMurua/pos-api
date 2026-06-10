using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

/// <summary>
/// Durable queue of outbound transactional emails (PR-5). Replaces the fire-and-forget
/// EmailService call with an enqueue + a <c>NotificationDispatchWorker</c> that sends with
/// retries/backoff. The row itself is the audit of event-driven notifications (a Sent row with
/// SentAtUtc + TemplateCode + BusinessId) — only MANUAL admin sends also write BusinessAuditLog.
/// See docs/saas-billing-architecture.md §4.11/§10.
/// </summary>
public class NotificationOutbox
{
    public int Id { get; set; }

    /// <summary>Owning business for context/traceability; null for non-tenant mail.</summary>
    public int? BusinessId { get; set; }

    [Required, MaxLength(50)]
    public string TemplateCode { get; set; } = null!;

    public NotificationRecipientType RecipientType { get; set; }

    /// <summary>Resolved at enqueue time (see <see cref="NotificationRecipientType"/>).</summary>
    [Required, MaxLength(150)]
    public string ToEmail { get; set; } = null!;

    /// <summary>JSON dictionary of template parameters.</summary>
    public string PayloadJson { get; set; } = "{}";

    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

    public int Attempts { get; set; }

    public DateTime NextAttemptAtUtc { get; set; }

    [MaxLength(500)]
    public string? LastError { get; set; }

    /// <summary>
    /// Optional dedup token, set only by daily-job enqueues (e.g. trial reminders) so a job
    /// re-run does not double-send. A partial unique index enforces it. Null ⇒ no dedup.
    /// </summary>
    [MaxLength(100)]
    public string? DedupKey { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }
}
