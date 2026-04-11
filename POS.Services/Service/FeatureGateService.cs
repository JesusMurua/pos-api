using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models.Catalogs;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Evaluates the Plan × BusinessType feature matrix with per-business memory caching.
/// </summary>
public class FeatureGateService : IFeatureGateService
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public FeatureGateService(ApplicationDbContext context, IMemoryCache cache)
    {
        _context = context;
        _cache = cache;
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
        _cache.Remove(CacheKey(businessId));
    }

    private Task<BusinessFeatureSnapshot> GetSnapshotAsync(int businessId)
    {
        return _cache.GetOrCreateAsync(CacheKey(businessId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            return await BuildSnapshotAsync(businessId);
        })!;
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
                b.BusinessTypeId,
                GiroIds = b.BusinessGiros.Select(bg => bg.BusinessTypeId).ToList()
            })
            .FirstOrDefaultAsync();

        if (business == null)
        {
            return BusinessFeatureSnapshot.Empty(PlanTypeIds.ToCode(PlanTypeIds.Free));
        }

        var activeGiroIds = business.GiroIds.Count > 0
            ? business.GiroIds
            : new List<int> { business.BusinessTypeId };

        var planRows = await _context.Set<PlanFeatureMatrix>()
            .AsNoTracking()
            .Where(m => m.PlanTypeId == business.PlanTypeId)
            .ToListAsync();

        var giroRows = await _context.Set<BusinessTypeFeature>()
            .AsNoTracking()
            .Where(b => activeGiroIds.Contains(b.BusinessTypeId))
            .ToListAsync();

        var overrideRows = await _context.Set<PlanBusinessTypeFeatureOverride>()
            .AsNoTracking()
            .Where(o => o.PlanTypeId == business.PlanTypeId && activeGiroIds.Contains(o.BusinessTypeId))
            .ToListAsync();

        var features = await _context.Set<FeatureCatalog>()
            .AsNoTracking()
            .ToListAsync();

        var planByFeatureId = planRows.ToDictionary(r => r.FeatureId);

        // Per-feature, per-giro applicability + optional limit override.
        var giroApplicability = giroRows
            .GroupBy(r => r.FeatureId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(r => r.BusinessTypeId, r => r.Limit));

        // Per-feature, per-giro absolute enablement override (wins over 2D resolution).
        var overrideByKey = overrideRows
            .ToDictionary(o => (o.FeatureId, o.BusinessTypeId), o => o.IsEnabled);

        var entries = new Dictionary<FeatureKey, FeatureEntry>(features.Count);
        var resourceLabels = new Dictionary<FeatureKey, string?>(features.Count);

        foreach (var feature in features)
        {
            var planEnabled = planByFeatureId.TryGetValue(feature.Id, out var planRow) && planRow.IsEnabled;
            giroApplicability.TryGetValue(feature.Id, out var applicableGiros);

            var isEnabled = false;
            foreach (var giroId in activeGiroIds)
            {
                bool giroEnabled;
                if (overrideByKey.TryGetValue((feature.Id, giroId), out var overrideEnabled))
                {
                    giroEnabled = overrideEnabled;
                }
                else
                {
                    var applicable = applicableGiros?.ContainsKey(giroId) ?? false;
                    giroEnabled = applicable && planEnabled;
                }

                if (giroEnabled)
                {
                    isEnabled = true;
                    break;
                }
            }

            var limitOverrides = applicableGiros?.Values.ToList();
            var limit = ResolveLimit(planRow?.DefaultLimit, limitOverrides);

            entries[feature.Key] = new FeatureEntry(isEnabled, limit);
            resourceLabels[feature.Key] = feature.ResourceLabel;
        }

        return new BusinessFeatureSnapshot(
            PlanCode: PlanTypeIds.ToCode(business.PlanTypeId),
            Entries: entries,
            ResourceLabels: resourceLabels);
    }

    private static int? ResolveLimit(int? planDefault, List<int?>? overrides)
    {
        if (planDefault == null) return null;
        if (overrides == null || overrides.Count == 0) return planDefault;

        var validOverrides = overrides.Where(o => o.HasValue).Select(o => o!.Value).ToList();
        if (validOverrides.Count == 0) return planDefault;

        return Math.Max(planDefault.Value, validOverrides.Max());
    }

    private static string CacheKey(int businessId) => $"FeatureGate::{businessId}";

    private sealed record FeatureEntry(bool IsEnabled, int? Limit);

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
