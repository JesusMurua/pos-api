namespace POS.Domain.DTOs.Common;

/// <summary>
/// Uniform pagination envelope for list endpoints. Carries the slice the
/// caller asked for plus the unpaginated total so the client can render
/// page indicators without an extra COUNT round-trip. Designed to be
/// reused across every paginated admin / ops endpoint — the shape stays
/// stable as new sources are added.
/// </summary>
/// <typeparam name="T">Row DTO type for the current page.</typeparam>
/// <param name="Items">Rows for the requested page, already ordered.</param>
/// <param name="TotalCount">Total rows that match the filter, ignoring pagination.</param>
/// <param name="PageSize">Echo of the requested (and clamped) page size.</param>
/// <param name="PageNumber">1-based page index the caller asked for.</param>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int PageSize,
    int PageNumber);
