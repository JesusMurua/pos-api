using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Payload for <c>PATCH /api/Admin/businesses/{id}/trial</c>. The new
/// date must be a future instant and within 180 days of now — the
/// controller rejects past dates and instants beyond the 6-month cap.
/// </summary>
public sealed record AdminExtendTrialRequest
{
    /// <summary>
    /// New trial-end instant in ISO 8601 with offset (e.g.
    /// <c>2026-09-01T00:00:00Z</c>). Parsed server-side; invalid strings
    /// surface as a 400 from MVC model binding.
    /// </summary>
    [Required]
    public DateTime NewTrialEndsAt { get; init; }

    /// <summary>Optional operator reason, recorded on the BusinessAuditLog row.</summary>
    [MaxLength(300)]
    public string? Reason { get; init; }
}
