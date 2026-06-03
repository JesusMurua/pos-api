using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

/// <summary>
/// Append-only audit trail for every admin mutation of the feature matrices
/// (Plan / Macro / Cluster), overrides and feature-catalog metadata. One row per
/// changed entity, so a bulk PUT of N entries produces N rows.
/// </summary>
public class FeatureMatrixAuditLog
{
    public int Id { get; set; }

    public DateTime ChangedAt { get; set; }

    /// <summary>
    /// Hashed admin token id (the <c>token_id</c> claim, 8 hex chars) that made
    /// the change. The admin auth scheme is token-based with no user identity, so
    /// attribution is by token, not user.
    /// </summary>
    [MaxLength(64)]
    public string? ChangedByTokenId { get; set; }

    /// <summary>Which matrix was touched: <c>plan</c>, <c>macro</c>, <c>cluster</c>, <c>override</c>, <c>feature-catalog</c>.</summary>
    [Required, MaxLength(30)]
    public string Axis { get; set; } = null!;

    /// <summary>Stable identifier of the changed row (its composite key, e.g. <c>plan=3;feature=120</c>).</summary>
    [Required, MaxLength(120)]
    public string EntityKey { get; set; } = null!;

    /// <summary>Serialized JSON of the row before the change; null for inserts.</summary>
    public string? BeforeJson { get; set; }

    /// <summary>Serialized JSON of the row after the change; null for deletes.</summary>
    public string? AfterJson { get; set; }
}
