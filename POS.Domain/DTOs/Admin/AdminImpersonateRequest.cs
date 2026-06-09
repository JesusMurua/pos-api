using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Optional payload for <c>POST /api/Admin/businesses/{id}/impersonate</c>. The
/// endpoint works with an empty body; when supplied, <see cref="Reason"/> is
/// recorded on the <c>BusinessAuditLog</c> row for the (security-sensitive)
/// impersonation trail.
/// </summary>
public sealed record AdminImpersonateRequest
{
    [MaxLength(300)]
    public string? Reason { get; init; }
}
