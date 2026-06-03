namespace POS.Domain.DTOs.Admin;

/// <summary>Read model for a feature catalog row (admin grid).</summary>
public sealed record FeatureCatalogDto(
    int Id,
    string Code,
    string Key,
    string Name,
    string? Description,
    bool IsQuantitative,
    string? ResourceLabel,
    int SortOrder,
    string Scope);

/// <summary>Editable metadata of a feature (the only mutable part — Key/Code are enum-bound).</summary>
public sealed record UpdateFeatureCatalogMetadataRequest(
    string Name,
    string? Description,
    string? ResourceLabel,
    int SortOrder);

/// <summary>Plan × Feature entry — a flag matrix (always one row per pair).</summary>
public sealed record PlanFeatureEntryDto(
    int PlanTypeId,
    int FeatureId,
    bool IsEnabled,
    int? DefaultLimit);

/// <summary>Macro × Feature entry — presence-based applicability plus an optional limit.</summary>
public sealed record MacroFeatureEntryDto(
    int MacroCategoryId,
    int FeatureId,
    bool IsApplicable,
    int? Limit);

/// <summary>Cluster × Feature entry — pure presence applicability.</summary>
public sealed record ClusterFeatureEntryDto(
    string ClusterCode,
    int FeatureId,
    bool IsApplicable);

/// <summary>Envelope for the cluster matrix GET so the UI knows the full slug list.</summary>
public sealed record ClusterMatrixDto(
    IReadOnlyList<string> Clusters,
    IReadOnlyList<ClusterFeatureRowDto> Rows);

/// <summary>An existing (applicable) cluster rule.</summary>
public sealed record ClusterFeatureRowDto(string ClusterCode, int FeatureId);

/// <summary>An existing macro applicability rule.</summary>
public sealed record MacroFeatureRowDto(int MacroCategoryId, int FeatureId, int? Limit);

/// <summary>Plan × Macro × Feature override (final word for that tuple).</summary>
public sealed record OverrideDto(
    int PlanTypeId,
    int MacroCategoryId,
    int FeatureId,
    bool IsEnabled);

/// <summary>Impact preview for a pending cluster-rule flip.</summary>
public sealed record PreviewImpactDto(
    int AffectedCount,
    IReadOnlyDictionary<string, int> BreakdownByPlan,
    IReadOnlyList<int> SampleBusinessIds);

/// <summary>One audit-trail row.</summary>
public sealed record FeatureMatrixAuditEntryDto(
    int Id,
    DateTime ChangedAt,
    string? ChangedByTokenId,
    string Axis,
    string EntityKey,
    string? BeforeJson,
    string? AfterJson);

/// <summary>Paged audit-log response.</summary>
public sealed record PagedAuditLogDto(
    int Page,
    int PageSize,
    int TotalRows,
    IReadOnlyList<FeatureMatrixAuditEntryDto> Items);
