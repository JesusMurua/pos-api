using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

/// <summary>
/// Inbox table for payment provider webhook events (Clip, MercadoPago).
/// Ensures idempotent, ordered processing via the Background Worker pattern.
/// </summary>
public class PaymentWebhookInbox
{
    public int Id { get; set; }

    /// <summary>Payment provider name: "MercadoPago", "Clip".</summary>
    [Required]
    [MaxLength(30)]
    public string Provider { get; set; } = null!;

    /// <summary>External event ID from the provider. Combined with Provider for idempotency.</summary>
    [Required]
    [MaxLength(255)]
    public string ExternalEventId { get; set; } = null!;

    /// <summary>Event type from the provider (e.g., "payment.created", "payment.updated").</summary>
    [Required]
    [MaxLength(100)]
    public string EventType { get; set; } = null!;

    /// <summary>Full raw JSON payload from the webhook for reprocessing.</summary>
    [Required]
    public string RawPayload { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAt { get; set; }

    /// <summary>Error details when Status is "failed", for manual inspection.</summary>
    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }
}
