using POS.Domain.DTOs.Admin;

namespace POS.Services.IService;

/// <summary>
/// Admin/ops operations to read and edit the feature matrices (Plan / Macro /
/// Cluster), overrides and feature-catalog metadata. Every mutation is audited
/// and bumps the feature cache via <see cref="IFeatureGateService.InvalidateAll"/>.
/// Bulk writes are upsert-merge: provided entries are applied, unlisted rows are
/// left untouched. Presence matrices (macro/cluster) treat <c>IsApplicable=false</c>
/// as "remove the row".
/// </summary>
public interface IFeatureMatrixAdminService
{
    Task<IReadOnlyList<FeatureCatalogDto>> GetFeatureCatalogAsync();
    Task UpdateFeatureMetadataAsync(int id, UpdateFeatureCatalogMetadataRequest request, string? tokenId);

    Task<IReadOnlyList<PlanFeatureEntryDto>> GetPlanMatrixAsync();
    Task BulkUpsertPlanMatrixAsync(IReadOnlyList<PlanFeatureEntryDto> entries, string? tokenId);

    Task<IReadOnlyList<MacroFeatureRowDto>> GetMacroMatrixAsync();
    Task BulkUpsertMacroMatrixAsync(IReadOnlyList<MacroFeatureEntryDto> entries, string? tokenId);

    Task<ClusterMatrixDto> GetClusterMatrixAsync();
    Task BulkUpsertClusterMatrixAsync(IReadOnlyList<ClusterFeatureEntryDto> entries, string? tokenId);

    Task<IReadOnlyList<OverrideDto>> GetOverridesAsync();
    Task CreateOverrideAsync(OverrideDto dto, string? tokenId);
    Task UpdateOverrideAsync(OverrideDto dto, string? tokenId);
    Task DeleteOverrideAsync(int planTypeId, int macroCategoryId, int featureId, string? tokenId);

    /// <summary>Impact of flipping a cluster rule, recomputed with the resolver (axis = cluster only).</summary>
    Task<PreviewImpactDto> PreviewClusterImpactAsync(string clusterCode, int featureId, bool isApplicable);

    Task<PagedAuditLogDto> GetAuditLogAsync(DateTime? from, DateTime? to, string? axis, int page, int pageSize);
}
