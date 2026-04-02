using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

/// <summary>
/// Inbox table for Stripe webhook events. Ensures idempotent, ordered processing
/// via the Background Worker pattern.
/// </summary>
public class StripeEventInbox
{
    public int Id { get; set; }

    /// <summary>Stripe event ID (e.g. evt_1abc...). Unique constraint prevents duplicate processing.</summary>
    [Required]
    [MaxLength(255)]
    public string StripeEventId { get; set; } = null!;

    /// <summary>Stripe event type (e.g. checkout.session.completed).</summary>
    [Required]
    [MaxLength(100)]
    public string Type { get; set; } = null!;

    /// <summary>Full raw JSON payload from the webhook for reprocessing.</summary>
    [Required]
    public string RawJson { get; set; } = null!;

    public StripeEventStatus Status { get; set; } = StripeEventStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    /// <summary>Error details when Status is Failed, for manual inspection.</summary>
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }
}
