namespace POS.Domain.DTOs.Admin;

/// <summary>
/// One <c>BusinessAuditLog</c> row (the explicit operator-action trail). The
/// <c>action</c> is the stable PascalCase enum name; <c>beforeJson</c>/<c>afterJson</c>
/// are opaque server-written JSON strings — the client renders them raw, it does not
/// type-parse them (see docs/saas-billing-api.md §7).
/// </summary>
public sealed record BusinessAuditEntryDto(
    int Id,
    int BusinessId,
    string Action,
    DateTime ChangedAtUtc,
    string? ChangedByTokenIdHash,
    string? Reason,
    string? BeforeJson,
    string? AfterJson);

/// <summary>Paged audit-log response (mirrors the FeatureMatrix audit envelope).</summary>
public sealed record PagedBusinessAuditLogDto(
    int Page,
    int PageSize,
    int TotalRows,
    IReadOnlyList<BusinessAuditEntryDto> Items);
