using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

/// <summary>
/// Append-only audit trail for every admin mutation of the payment-method catalog,
/// plan matrix and tenant overrides. One row per changed entity. Independent from
/// <see cref="FeatureMatrixAuditLog"/> so each matrix keeps its own history.
/// </summary>
public class PaymentMatrixAuditLog
{
    public int Id { get; set; }

    public DateTime ChangedAt { get; set; }

    /// <summary>Hashed admin token id (the <c>token_id</c> claim) that made the change.</summary>
    [MaxLength(64)]
    public string? ChangedByTokenId { get; set; }

    /// <summary>Which surface was touched: <c>catalog</c>, <c>plan</c>, <c>override</c>.</summary>
    [Required, MaxLength(30)]
    public string Axis { get; set; } = null!;

    /// <summary>Stable identifier of the changed row (e.g. <c>plan=3;method=2</c>).</summary>
    [Required, MaxLength(120)]
    public string EntityKey { get; set; } = null!;

    /// <summary>Serialized JSON before the change; null for inserts.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>Serialized JSON after the change; null for deletes.</summary>
    public string? AfterJson { get; set; }
}
