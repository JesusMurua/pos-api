using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Payload for <c>POST /api/Admin/businesses/{id}/reset-owner-password</c>.
/// When <see cref="NewPassword"/> is null the backend generates a
/// 12-character cryptographically-random password and surfaces it on the
/// response so the admin can copy + relay to the customer.
/// </summary>
public sealed record AdminResetOwnerPasswordRequest
{
    [MinLength(8)]
    public string? NewPassword { get; init; }

    /// <summary>Optional operator reason, recorded on the BusinessAuditLog row.</summary>
    [MaxLength(300)]
    public string? Reason { get; init; }
}
