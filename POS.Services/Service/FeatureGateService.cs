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
                b.PrimaryMacroCategoryId
            })
            .FirstOrDefaultAsync();

        if (business == null)
        {
            return BusinessFeatureSnapshot.Empty(PlanTypeIds.ToCode(PlanTypeIds.Free));
        }

        var macroCategoryId = business.PrimaryMacroCategoryId;

        var planRows = await _context.Set<PlanFeatureMatrix>()
            .AsNoTracking()
            .Where(m => m.PlanTypeId == business.PlanTypeId)
            .ToListAsync();

        var macroRows = await _context.Set<BusinessTypeFeature>()
            .AsNoTracking()
            .Where(b => b.MacroCategoryId == macroCategoryId)
            .ToListAsync();

        var overrideRows = await _context.Set<PlanBusinessTypeFeatureOverride>()
            .AsNoTracking()
            .Where(o => o.PlanTypeId == business.PlanTypeId && o.MacroCategoryId == macroCategoryId)
            .ToListAsync();

        var features = await _context.Set<FeatureCatalog>()
            .AsNoTracking()
            .ToListAsync();

        var planByFeatureId = planRows.ToDictionary(r => r.FeatureId);
        var macroApplicability = macroRows.ToDictionary(r => r.FeatureId, r => r.Limit);
        var overrideByFeatureId = overrideRows.ToDictionary(o => o.FeatureId, o => o.IsEnabled);

        var entries = new Dictionary<FeatureKey, FeatureEntry>(features.Count);
        var resourceLabels = new Dictionary<FeatureKey, string?>(features.Count);

        foreach (var feature in features)
        {
            var planEnabled = planByFeatureId.TryGetValue(feature.Id, out var planRow) && planRow.IsEnabled;
            var applicable = macroApplicability.TryGetValue(feature.Id, out var macroLimit);

            bool isEnabled;
            if (overrideByFeatureId.TryGetValue(feature.Id, out var overrideEnabled))
            {
                isEnabled = overrideEnabled;
            }
            else
            {
                isEnabled = applicable && planEnabled;
            }

            var limit = ResolveLimit(planRow?.DefaultLimit, applicable ? macroLimit : null);

            entries[feature.Key] = new FeatureEntry(isEnabled, limit);
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
