using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Payload for <c>PATCH /api/Admin/businesses/{id}/status</c>. Setting
/// <see cref="IsActive"/> to <c>false</c> immediately blocks the tenant's
/// owner / staff from authenticating via email or PIN login (gate enforced
/// in <c>AuthService</c>). <see cref="Reason"/> is logged for forensics
/// but not persisted in v1 — when the AuditLog refactor lands the field
/// will reach the dedicated table.
/// </summary>
public sealed record AdminToggleBusinessStatusRequest
{
    [Required]
    public bool IsActive { get; init; }

    [MaxLength(500)]
    public string? Reason { get; init; }
}
