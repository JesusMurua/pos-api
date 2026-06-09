using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Full-replace payload for <c>PUT /api/Admin/plan-types/{id}</c>. The FE loads
/// the current plan, edits, and submits every editable field. <c>Code</c> and
/// <c>Id</c> are immutable (the catalog freeze keys) and are not part of this DTO.
/// <see cref="MonthlyPrice"/> is admin-owned and durable — the boot reseed no
/// longer overwrites it (OQ-3). See docs/saas-billing-architecture.md §12.
/// </summary>
public sealed record AdminUpdatePlanTypeRequest
{
    [Required, MaxLength(50)]
    public string Name { get; init; } = null!;

    public int SortOrder { get; init; }

    /// <summary>ISO 4217 code (e.g. "MXN"). Ready for multi-currency (OQ-10).</summary>
    [Required, MaxLength(3)]
    public string Currency { get; init; } = null!;

    /// <summary>Monthly price in <see cref="Currency"/>. Null = unpriced (Enterprise / contact-sales).</summary>
    public decimal? MonthlyPrice { get; init; }
}
