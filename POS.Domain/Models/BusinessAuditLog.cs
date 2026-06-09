using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

/// <summary>
/// Append-only, admin-explicit audit trail of operator actions on a tenant
/// (suspend, plan change, trial extension, password reset, impersonation, create…).
/// Distinct from the transparent per-field <c>AuditInterceptor</c> (which mirrors
/// tenant POS-entity CRUD and never touches <see cref="Business"/>). One row per
/// action, carrying the operator's hashed token id + optional reason. Mirrors the
/// <see cref="FeatureMatrixAuditLog"/> / <see cref="PaymentMatrixAuditLog"/> shape.
/// </summary>
public class BusinessAuditLog
{
    public int Id { get; set; }

    /// <summary>The tenant the action targeted. FK → Business (RESTRICT).</summary>
    public int BusinessId { get; set; }

    /// <summary>The operator action. Stored as a string via HasConversion.</summary>
    public BusinessAuditAction Action { get; set; }

    /// <summary>Optional operator-supplied reason (commercial / support context).</summary>
    [MaxLength(300)]
    public string? Reason { get; set; }

    /// <summary>Serialized JSON of the relevant fields before the change; null when N/A.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>Serialized JSON of the relevant fields after the change; null when N/A.</summary>
    public string? AfterJson { get; set; }

    /// <summary>Hashed admin token id (the <c>token_id</c> claim) that performed the action.</summary>
    [MaxLength(64)]
    public string? ChangedByTokenId { get; set; }

    public DateTime ChangedAtUtc { get; set; }
}
