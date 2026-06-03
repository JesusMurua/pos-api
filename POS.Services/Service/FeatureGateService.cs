using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Domain.Models.Catalogs;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Evaluates the Plan × Macro × Cluster feature matrix. The matrices are global
/// and small, so they are loaded once into a shared, generation-versioned cache;
/// each per-business snapshot then resolves in memory against that cache plus the
/// tenant's own plan/macro/clusters. <see cref="InvalidateAll"/> bumps the
/// generation to drop every cached entry at once.
/// </summary>
public class FeatureGateService : IFeatureGateService
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly FeatureCacheGeneration _generation;

    private static readonly TimeSpan SnapshotTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MatricesTtl = TimeSpan.FromHours(1);

    public FeatureGateService(ApplicationDbContext context, IMemoryCache cache, FeatureCacheGeneration generation)
    {
        _context = context;
        _cache = cache;
        _generation = generation;
    }

    public async Task<bool> IsEnabledAsync(int businessId, FeatureKey feature)
    {
        var snapshot = await GetSnapshotAsync(businessId);
        return snapshot.Entries.TryGetValue(feature, out var entry) && entry.IsEnabled;
    }

    public async Task<int?> GetLimitAsync(int businessId, FeatureKey feature)
    {
        var snapshot = await GetSnapshotAsync(businessId);
        return snapshot.Entries.TryGetValue(feature, out var entry) ? entry.Limit : null;
    }

    public async Task<EnforcementScope> GetScopeAsync(int businessId, FeatureKey feature)
    {
        var snapshot = await GetSnapshotAsync(businessId);
        return snapshot.Entries.TryGetValue(feature, out var entry) ? entry.Scope : EnforcementScope.Global;
    }

    public async Task<(int? Limit, EnforcementScope Scope)> GetEnforcementInfoAsync(int businessId, FeatureKey feature)
    {
        var snapshot = await GetSnapshotAsync(businessId);
        if (snapshot.Entries.TryGetValue(feature, out var entry))
            return (entry.Limit, entry.Scope);
        return (null, EnforcementScope.Global);
    }

    public async Task<IReadOnlyList<string>> GetEnabledFeaturesAsync(int businessId)
    {
        var snapshot = await GetSnapshotAsync(businessId);
        return snapshot.Entries
            .Where(kv => kv.Value.IsEnabled)
            .Select(kv => kv.Key.ToString())
            .ToList();
    }

    public async Task EnforceAsync(int businessId, FeatureKey feature, int? currentUsage = null)
    {
        var snapshot = await GetSnapshotAsync(businessId);

        if (!snapshot.Entries.TryGetValue(feature, out var entry) || !entry.IsEnabled)
        {
            var label = snapshot.ResourceLabelFor(feature) ?? feature.ToString();
            throw new PlanLimitExceededException(label, 0, snapshot.PlanCode);
        }

        if (currentUsage.HasValue && entry.Limit.HasValue && currentUsage.Value >= entry.Limit.Value)
        {
            var label = snapshot.ResourceLabelFor(feature) ?? feature.ToString();
            throw new PlanLimitExceededException(label, entry.Limit.Value, snapshot.PlanCode);
        }
    }

    public void Invalidate(int businessId)
    {
        _cache.Remove(SnapshotKey(_generation.Current, businessId));
    }

    public void InvalidateAll()
    {
        // Advancing the generation orphans every snapshot key and the global
        // matrix key in one move — both embed the generation. Orphans expire by
        // their own TTL; the next read repopulates under the new generation.
        _generation.Bump();
    }

    private Task<BusinessFeatureSnapshot> GetSnapshotAsync(int businessId)
    {
        return _cache.GetOrCreateAsync(SnapshotKey(_generation.Current, businessId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = SnapshotTtl;
            return await BuildSnapshotAsync(businessId);
        })!;
    }

    /// <summary>
    /// Loads the global feature matrices once per generation into a shared cache.
    /// They are small and tenant-independent, so every per-business snapshot
    /// resolves against this in-memory copy instead of re-querying the DB.
    /// </summary>
    private Task<FeatureMatrixData> GetMatricesAsync()
    {
        return _cache.GetOrCreateAsync(MatricesKey(_generation.Current), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = MatricesTtl;
            return await LoadMatricesAsync();
        })!;
    }

    private async Task<FeatureMatrixData> LoadMatricesAsync()
    {
        var features = await _context.Set<FeatureCatalog>().AsNoTracking().ToListAsync();
        var planRows = await _context.Set<PlanFeatureMatrix>().AsNoTracking().ToListAsync();
        var macroRows = await _context.Set<BusinessTypeFeature>().AsNoTracking().ToListAsync();
        var overrideRows = await _context.Set<PlanBusinessTypeFeatureOverride>().AsNoTracking().ToListAsync();
        var clusterRows = await _context.Set<ClusterFeature>().AsNoTracking().ToListAsync();

        var planByPlan = planRows
            .GroupBy(r => r.PlanTypeId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.FeatureId));

        var macroByMacro = macroRows
            .GroupBy(r => r.MacroCategoryId)
            .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.FeatureId, r => r.Limit));

        var overrideByPlanMacro = overrideRows
            .GroupBy(r => (r.PlanTypeId, r.MacroCategoryId))
            .ToDictionary(g => g.Key, g => g.ToDictionary(r => r.FeatureId, r => r.IsEnabled));

        var clustersByFeatureId = clusterRows
            .GroupBy(r => r.FeatureId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.ClusterCode).ToHashSet(StringComparer.Ordinal));

        return new FeatureMatrixData(features, planByPlan, macroByMacro, overrideByPlanMacro, clustersByFeatureId);
    }

    private async Task<BusinessFeatureSnapshot> BuildSnapshotAsync(int businessId)
    {
        var business = await _context.Businesses
            .AsNoTracking()
            .Where(b => b.Id == businessId)
            .Select(b => new
            {
                b.Id,
                b.PlanTypeId,
                b.PrimaryMacroCategoryId
            })
            .FirstOrDefaultAsync();

        if (business == null)
        {
            return BusinessFeatureSnapshot.Empty(PlanTypeIds.ToCode(PlanTypeIds.Free));
        }

        var macroCategoryId = business.PrimaryMacroCategoryId;

        var matrices = await GetMatricesAsync();

        // Per-tenant data: the business's clusters come from its catalog sub-giros
        // (BusinessGiro → BusinessTypeCatalog.ClusterCode).
        var businessClusters = await _context.Set<BusinessGiro>()
            .AsNoTracking()
            .Where(g => g.BusinessId == businessId && g.BusinessTypeCatalog!.ClusterCode != null)
            .Select(g => g.BusinessTypeCatalog!.ClusterCode!)
            .Distinct()
            .ToListAsync();

        var planByFeatureId = matrices.PlanByPlan.GetValueOrDefault(business.PlanTypeId)
            ?? EmptyPlanRows;
        var macroApplicability = matrices.MacroByMacro.GetValueOrDefault(macroCategoryId)
            ?? EmptyMacroLimits;
        var overrideByFeatureId = matrices.OverrideByPlanMacro.GetValueOrDefault((business.PlanTypeId, macroCategoryId))
            ?? EmptyOverrides;
        var clustersByFeatureId = matrices.ClustersByFeatureId;
        var businessClusterSet = businessClusters.ToHashSet(StringComparer.Ordinal);

        var entries = new Dictionary<FeatureKey, FeatureEntry>(matrices.Features.Count);
        var resourceLabels = new Dictionary<FeatureKey, string?>(matrices.Features.Count);

        foreach (var feature in matrices.Features)
        {
            var planEnabled = planByFeatureId.TryGetValue(feature.Id, out var planRow) && planRow.IsEnabled;
            var applicable = macroApplicability.TryGetValue(feature.Id, out var macroLimit);

            // Cluster axis (additive, fail-closed). Features without any cluster
            // rule resolve as before (clusterApplies = true). A cluster-ruled
            // feature requires the business to carry at least one matching
            // cluster; a business with no clusters (e.g. mid-onboarding before
            // its sub-giro is set) gets it disabled rather than leaked.
            bool clusterApplies;
            if (!clustersByFeatureId.TryGetValue(feature.Id, out var featureClusters))
                clusterApplies = true;
            else if (businessClusterSet.Count == 0)
                clusterApplies = false;
            else
                clusterApplies = featureClusters.Overlaps(businessClusterSet);

            bool isEnabled;
            if (overrideByFeatureId.TryGetValue(feature.Id, out var overrideEnabled))
            {
                isEnabled = overrideEnabled;
            }
            else
            {
                isEnabled = applicable && planEnabled && clusterApplies;
            }

            var limit = ResolveLimit(planRow?.DefaultLimit, applicable ? macroLimit : null);

            entries[feature.Key] = new FeatureEntry(isEnabled, limit, feature.Scope);
            resourceLabels[feature.Key] = feature.ResourceLabel;
        }

        return new BusinessFeatureSnapshot(
            PlanCode: PlanTypeIds.ToCode(business.PlanTypeId),
            Entries: entries,
            ResourceLabels: resourceLabels);
    }

    private static int? ResolveLimit(int? planDefault, int? macroOverride)
    {
        if (planDefault == null) return null;
        if (macroOverride == null) return planDefault;
        return Math.Max(planDefault.Value, macroOverride.Value);
    }

    private static string SnapshotKey(long generation, int businessId) => $"FeatureGate::{generation}::{businessId}";

    private static string MatricesKey(long generation) => $"FeatureMatrix::{generation}";

    private static readonly Dictionary<int, PlanFeatureMatrix> EmptyPlanRows = new();
    private static readonly Dictionary<int, int?> EmptyMacroLimits = new();
    private static readonly Dictionary<int, bool> EmptyOverrides = new();

    /// <summary>
    /// In-memory, tenant-independent projection of the four feature matrices,
    /// shaped for O(1) per-tenant resolution. Cached once per generation.
    /// </summary>
    private sealed record FeatureMatrixData(
        IReadOnlyList<FeatureCatalog> Features,
        IReadOnlyDictionary<int, Dictionary<int, PlanFeatureMatrix>> PlanByPlan,
        IReadOnlyDictionary<int, Dictionary<int, int?>> MacroByMacro,
        IReadOnlyDictionary<(int PlanTypeId, int MacroCategoryId), Dictionary<int, bool>> OverrideByPlanMacro,
        IReadOnlyDictionary<int, HashSet<string>> ClustersByFeatureId);

    private sealed record FeatureEntry(bool IsEnabled, int? Limit, EnforcementScope Scope);

    private sealed record BusinessFeatureSnapshot(
        string PlanCode,
        Dictionary<FeatureKey, FeatureEntry> Entries,
        Dictionary<FeatureKey, string?> ResourceLabels)
    {
        public string? ResourceLabelFor(FeatureKey feature) =>
            ResourceLabels.TryGetValue(feature, out var label) ? label : null;

        public static BusinessFeatureSnapshot Empty(string planCode) =>
            new(planCode, new Dictionary<FeatureKey, FeatureEntry>(), new Dictionary<FeatureKey, string?>());
    }
}
