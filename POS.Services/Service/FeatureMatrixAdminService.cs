using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POS.Domain.DTOs.Admin;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Domain.Models.Catalogs;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class FeatureMatrixAdminService : IFeatureMatrixAdminService
{
    private readonly ApplicationDbContext _context;
    private readonly IFeatureGateService _featureGate;

    private static readonly HashSet<int> ValidPlanIds =
        new() { PlanTypeIds.Free, PlanTypeIds.Basic, PlanTypeIds.Pro, PlanTypeIds.Enterprise };

    private static readonly HashSet<int> ValidMacroIds = new()
    {
        MacroCategoryIds.FoodBeverage, MacroCategoryIds.QuickService,
        MacroCategoryIds.Retail, MacroCategoryIds.Services
    };

    public FeatureMatrixAdminService(ApplicationDbContext context, IFeatureGateService featureGate)
    {
        _context = context;
        _featureGate = featureGate;
    }

    // ── Feature catalog ───────────────────────────────────────────────

    public async Task<IReadOnlyList<FeatureCatalogDto>> GetFeatureCatalogAsync()
    {
        var rows = await _context.Set<FeatureCatalog>().AsNoTracking()
            .OrderBy(f => f.SortOrder).ToListAsync();
        return rows.Select(f => new FeatureCatalogDto(
            f.Id, f.Code, f.Key.ToString(), f.Name, f.Description,
            f.IsQuantitative, f.ResourceLabel, f.SortOrder, f.Scope.ToString())).ToList();
    }

    public async Task UpdateFeatureMetadataAsync(int id, UpdateFeatureCatalogMetadataRequest request, string? tokenId)
    {
        var row = await _context.Set<FeatureCatalog>().FirstOrDefaultAsync(f => f.Id == id)
            ?? throw new NotFoundException($"Feature {id} not found");

        var before = JsonSerializer.Serialize(new { row.Name, row.Description, row.ResourceLabel, row.SortOrder });

        row.Name = request.Name;
        row.Description = request.Description;
        row.ResourceLabel = request.ResourceLabel;
        row.SortOrder = request.SortOrder;

        var after = JsonSerializer.Serialize(new { row.Name, row.Description, row.ResourceLabel, row.SortOrder });
        AddAudit("feature-catalog", $"feature={id}", before, after, tokenId);

        await SaveAndInvalidateAsync();
    }

    // ── Plan × Feature (flag matrix) ──────────────────────────────────

    public async Task<IReadOnlyList<PlanFeatureEntryDto>> GetPlanMatrixAsync()
    {
        var rows = await _context.Set<PlanFeatureMatrix>().AsNoTracking().ToListAsync();
        return rows.Select(r => new PlanFeatureEntryDto(r.PlanTypeId, r.FeatureId, r.IsEnabled, r.DefaultLimit)).ToList();
    }

    public async Task BulkUpsertPlanMatrixAsync(IReadOnlyList<PlanFeatureEntryDto> entries, string? tokenId)
    {
        if (entries.Count == 0) return;

        var featureIds = await ValidFeatureIdsAsync();
        foreach (var e in entries)
        {
            ValidatePlan(e.PlanTypeId);
            ValidateFeature(e.FeatureId, featureIds);
        }

        // Small tenant-independent table — load it fully (tracked) and match in memory.
        var existing = (await _context.Set<PlanFeatureMatrix>().ToListAsync())
            .ToDictionary(r => (r.PlanTypeId, r.FeatureId));

        foreach (var e in entries)
        {
            existing.TryGetValue((e.PlanTypeId, e.FeatureId), out var row);
            var before = row is null ? null
                : JsonSerializer.Serialize(new { row.IsEnabled, row.DefaultLimit });

            if (row is null)
            {
                row = new PlanFeatureMatrix { PlanTypeId = e.PlanTypeId, FeatureId = e.FeatureId };
                _context.Set<PlanFeatureMatrix>().Add(row);
            }
            row.IsEnabled = e.IsEnabled;
            row.DefaultLimit = e.DefaultLimit;

            var after = JsonSerializer.Serialize(new { row.IsEnabled, row.DefaultLimit });
            AddAudit("plan", $"plan={e.PlanTypeId};feature={e.FeatureId}", before, after, tokenId);
        }

        await SaveAndInvalidateAsync();
    }

    // ── Macro × Feature (presence + limit) ────────────────────────────

    public async Task<IReadOnlyList<MacroFeatureRowDto>> GetMacroMatrixAsync()
    {
        var rows = await _context.Set<BusinessTypeFeature>().AsNoTracking().ToListAsync();
        return rows.Select(r => new MacroFeatureRowDto(r.MacroCategoryId, r.FeatureId, r.Limit)).ToList();
    }

    public async Task BulkUpsertMacroMatrixAsync(IReadOnlyList<MacroFeatureEntryDto> entries, string? tokenId)
    {
        if (entries.Count == 0) return;

        var featureIds = await ValidFeatureIdsAsync();
        foreach (var e in entries)
        {
            ValidateMacro(e.MacroCategoryId);
            ValidateFeature(e.FeatureId, featureIds);
        }

        var existing = (await _context.Set<BusinessTypeFeature>().ToListAsync())
            .ToDictionary(r => (r.MacroCategoryId, r.FeatureId));

        foreach (var e in entries)
        {
            existing.TryGetValue((e.MacroCategoryId, e.FeatureId), out var row);
            var before = row is null ? null : JsonSerializer.Serialize(new { Applicable = true, row.Limit });
            var key = $"macro={e.MacroCategoryId};feature={e.FeatureId}";

            if (e.IsApplicable)
            {
                if (row is null)
                {
                    row = new BusinessTypeFeature { MacroCategoryId = e.MacroCategoryId, FeatureId = e.FeatureId };
                    _context.Set<BusinessTypeFeature>().Add(row);
                }
                row.Limit = e.Limit;
                AddAudit("macro", key, before, JsonSerializer.Serialize(new { Applicable = true, row.Limit }), tokenId);
            }
            else if (row is not null)
            {
                _context.Set<BusinessTypeFeature>().Remove(row);
                AddAudit("macro", key, before, null, tokenId);
            }
        }

        await SaveAndInvalidateAsync();
    }

    // ── Cluster × Feature (pure presence) ─────────────────────────────

    public async Task<ClusterMatrixDto> GetClusterMatrixAsync()
    {
        var rows = await _context.Set<ClusterFeature>().AsNoTracking().ToListAsync();
        return new ClusterMatrixDto(
            ClusterCodes.All.OrderBy(c => c).ToList(),
            rows.Select(r => new ClusterFeatureRowDto(r.ClusterCode, r.FeatureId)).ToList());
    }

    public async Task BulkUpsertClusterMatrixAsync(IReadOnlyList<ClusterFeatureEntryDto> entries, string? tokenId)
    {
        if (entries.Count == 0) return;

        var featureIds = await ValidFeatureIdsAsync();
        foreach (var e in entries)
        {
            ValidateCluster(e.ClusterCode);
            ValidateFeature(e.FeatureId, featureIds);
        }

        var existing = (await _context.Set<ClusterFeature>().ToListAsync())
            .ToDictionary(r => (r.ClusterCode, r.FeatureId));

        foreach (var e in entries)
        {
            existing.TryGetValue((e.ClusterCode, e.FeatureId), out var row);
            var key = $"cluster={e.ClusterCode};feature={e.FeatureId}";

            if (e.IsApplicable)
            {
                if (row is null)
                {
                    _context.Set<ClusterFeature>().Add(new ClusterFeature { ClusterCode = e.ClusterCode, FeatureId = e.FeatureId });
                    AddAudit("cluster", key, null, JsonSerializer.Serialize(new { Applicable = true }), tokenId);
                }
            }
            else if (row is not null)
            {
                _context.Set<ClusterFeature>().Remove(row);
                AddAudit("cluster", key, JsonSerializer.Serialize(new { Applicable = true }), null, tokenId);
            }
        }

        await SaveAndInvalidateAsync();
    }

    // ── Overrides (composite key CRUD) ────────────────────────────────

    public async Task<IReadOnlyList<OverrideDto>> GetOverridesAsync()
    {
        var rows = await _context.Set<PlanBusinessTypeFeatureOverride>().AsNoTracking().ToListAsync();
        return rows.Select(o => new OverrideDto(o.PlanTypeId, o.MacroCategoryId, o.FeatureId, o.IsEnabled)).ToList();
    }

    public async Task CreateOverrideAsync(OverrideDto dto, string? tokenId)
    {
        await ValidateOverrideKeyAsync(dto);
        if (await FindOverrideAsync(dto) is not null)
            throw new ValidationException("Override already exists; use PUT to update it.");

        _context.Set<PlanBusinessTypeFeatureOverride>().Add(new PlanBusinessTypeFeatureOverride
        {
            PlanTypeId = dto.PlanTypeId,
            MacroCategoryId = dto.MacroCategoryId,
            FeatureId = dto.FeatureId,
            IsEnabled = dto.IsEnabled
        });
        AddAudit("override", OverrideKey(dto), null, JsonSerializer.Serialize(new { dto.IsEnabled }), tokenId);

        await SaveAndInvalidateAsync();
    }

    public async Task UpdateOverrideAsync(OverrideDto dto, string? tokenId)
    {
        await ValidateOverrideKeyAsync(dto);
        var row = await FindOverrideAsync(dto)
            ?? throw new NotFoundException("Override not found; use POST to create it.");

        var before = JsonSerializer.Serialize(new { row.IsEnabled });
        row.IsEnabled = dto.IsEnabled;
        AddAudit("override", OverrideKey(dto), before, JsonSerializer.Serialize(new { row.IsEnabled }), tokenId);

        await SaveAndInvalidateAsync();
    }

    public async Task DeleteOverrideAsync(int planTypeId, int macroCategoryId, int featureId, string? tokenId)
    {
        var dto = new OverrideDto(planTypeId, macroCategoryId, featureId, false);
        var row = await FindOverrideAsync(dto)
            ?? throw new NotFoundException("Override not found");

        var before = JsonSerializer.Serialize(new { row.IsEnabled });
        _context.Set<PlanBusinessTypeFeatureOverride>().Remove(row);
        AddAudit("override", OverrideKey(dto), before, null, tokenId);

        await SaveAndInvalidateAsync();
    }

    // ── Preview impact (cluster axis only) ────────────────────────────

    public async Task<PreviewImpactDto> PreviewClusterImpactAsync(string clusterCode, int featureId, bool isApplicable)
    {
        ValidateCluster(clusterCode);
        ValidateFeature(featureId, await ValidFeatureIdsAsync());

        // Resolution inputs for this single feature.
        var planEnabledByPlan = (await _context.Set<PlanFeatureMatrix>().AsNoTracking()
                .Where(r => r.FeatureId == featureId).ToListAsync())
            .ToDictionary(r => r.PlanTypeId, r => r.IsEnabled);
        var applicableMacros = (await _context.Set<BusinessTypeFeature>().AsNoTracking()
                .Where(r => r.FeatureId == featureId).Select(r => r.MacroCategoryId).ToListAsync())
            .ToHashSet();
        var overrideByPlanMacro = (await _context.Set<PlanBusinessTypeFeatureOverride>().AsNoTracking()
                .Where(r => r.FeatureId == featureId).ToListAsync())
            .ToDictionary(r => (r.PlanTypeId, r.MacroCategoryId), r => r.IsEnabled);

        var currentClusters = (await _context.Set<ClusterFeature>().AsNoTracking()
                .Where(r => r.FeatureId == featureId).Select(r => r.ClusterCode).ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
        var newClusters = new HashSet<string>(currentClusters, StringComparer.Ordinal);
        if (isApplicable) newClusters.Add(clusterCode); else newClusters.Remove(clusterCode);

        // Candidates: normally only businesses carrying the flipped cluster can
        // change. But when the flip adds the FIRST rule or removes the LAST rule
        // for the feature, the gating status flips (un-gated ↔ gated), so every
        // business is potentially affected.
        var gatingStatusFlips = (currentClusters.Count > 0) != (newClusters.Count > 0);
        var candidateIds = gatingStatusFlips
            ? await _context.Businesses.AsNoTracking().Select(b => b.Id).ToListAsync()
            : await _context.Set<BusinessGiro>().AsNoTracking()
                .Where(g => g.BusinessTypeCatalog!.ClusterCode == clusterCode)
                .Select(g => g.BusinessId).Distinct().ToListAsync();

        if (candidateIds.Count == 0)
            return new PreviewImpactDto(0, new Dictionary<string, int>(), Array.Empty<int>());

        var businesses = await _context.Businesses.AsNoTracking()
            .Where(b => candidateIds.Contains(b.Id))
            .Select(b => new { b.Id, b.PlanTypeId, b.PrimaryMacroCategoryId })
            .ToListAsync();

        var clustersByBusiness = (await _context.Set<BusinessGiro>().AsNoTracking()
                .Where(g => candidateIds.Contains(g.BusinessId) && g.BusinessTypeCatalog!.ClusterCode != null)
                .Select(g => new { g.BusinessId, Cluster = g.BusinessTypeCatalog!.ClusterCode! })
                .ToListAsync())
            .GroupBy(x => x.BusinessId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Cluster).ToHashSet(StringComparer.Ordinal));

        var affected = new List<int>();
        var breakdown = new Dictionary<string, int>();

        foreach (var b in businesses)
        {
            var clusters = clustersByBusiness.GetValueOrDefault(b.Id) ?? new HashSet<string>(StringComparer.Ordinal);
            var planEnabled = planEnabledByPlan.GetValueOrDefault(b.PlanTypeId);
            var macroApplicable = applicableMacros.Contains(b.PrimaryMacroCategoryId);
            bool? ovr = overrideByPlanMacro.TryGetValue((b.PlanTypeId, b.PrimaryMacroCategoryId), out var ov) ? ov : null;

            var before = Resolve(planEnabled, macroApplicable, ovr, currentClusters, clusters);
            var after = Resolve(planEnabled, macroApplicable, ovr, newClusters, clusters);

            if (before != after)
            {
                affected.Add(b.Id);
                var code = PlanTypeIds.ToCode(b.PlanTypeId).ToLowerInvariant();
                breakdown[code] = breakdown.GetValueOrDefault(code) + 1;
            }
        }

        return new PreviewImpactDto(affected.Count, breakdown, affected.Take(50).ToList());
    }

    private static bool Resolve(bool planEnabled, bool macroApplicable, bool? overrideEnabled,
        HashSet<string> featureClusters, HashSet<string> businessClusters)
    {
        if (overrideEnabled.HasValue) return overrideEnabled.Value;

        bool clusterApplies;
        if (featureClusters.Count == 0) clusterApplies = true;
        else if (businessClusters.Count == 0) clusterApplies = false;
        else clusterApplies = featureClusters.Overlaps(businessClusters);

        return planEnabled && macroApplicable && clusterApplies;
    }

    // ── Audit log ─────────────────────────────────────────────────────

    public async Task<PagedAuditLogDto> GetAuditLogAsync(DateTime? from, DateTime? to, string? axis, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        var query = _context.Set<FeatureMatrixAuditLog>().AsNoTracking();
        if (from.HasValue) query = query.Where(a => a.ChangedAt >= from.Value);
        if (to.HasValue) query = query.Where(a => a.ChangedAt <= to.Value);
        if (!string.IsNullOrWhiteSpace(axis)) query = query.Where(a => a.Axis == axis);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.ChangedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new FeatureMatrixAuditEntryDto(
                a.Id, a.ChangedAt, a.ChangedByTokenId, a.Axis, a.EntityKey, a.BeforeJson, a.AfterJson))
            .ToListAsync();

        return new PagedAuditLogDto(page, pageSize, total, items);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private void AddAudit(string axis, string entityKey, string? before, string? after, string? tokenId)
    {
        _context.Set<FeatureMatrixAuditLog>().Add(new FeatureMatrixAuditLog
        {
            ChangedAt = DateTime.UtcNow,
            ChangedByTokenId = tokenId,
            Axis = axis,
            EntityKey = entityKey,
            BeforeJson = before,
            AfterJson = after
        });
    }

    private async Task SaveAndInvalidateAsync()
    {
        await _context.SaveChangesAsync();
        _featureGate.InvalidateAll();
    }

    private async Task<HashSet<int>> ValidFeatureIdsAsync() =>
        (await _context.Set<FeatureCatalog>().AsNoTracking().Select(f => f.Id).ToListAsync()).ToHashSet();

    private static void ValidatePlan(int planTypeId)
    {
        if (!ValidPlanIds.Contains(planTypeId))
            throw new ValidationException($"Unknown planTypeId {planTypeId}");
    }

    private static void ValidateMacro(int macroCategoryId)
    {
        if (!ValidMacroIds.Contains(macroCategoryId))
            throw new ValidationException($"Unknown macroCategoryId {macroCategoryId}");
    }

    private static void ValidateCluster(string clusterCode)
    {
        if (!ClusterCodes.All.Contains(clusterCode))
            throw new ValidationException($"Unknown clusterCode '{clusterCode}'");
    }

    private static void ValidateFeature(int featureId, HashSet<int> validFeatureIds)
    {
        if (!validFeatureIds.Contains(featureId))
            throw new ValidationException($"Unknown featureId {featureId}");
    }

    private async Task ValidateOverrideKeyAsync(OverrideDto dto)
    {
        ValidatePlan(dto.PlanTypeId);
        ValidateMacro(dto.MacroCategoryId);
        ValidateFeature(dto.FeatureId, await ValidFeatureIdsAsync());
    }

    private Task<PlanBusinessTypeFeatureOverride?> FindOverrideAsync(OverrideDto dto) =>
        _context.Set<PlanBusinessTypeFeatureOverride>()
            .FirstOrDefaultAsync(o => o.PlanTypeId == dto.PlanTypeId
                && o.MacroCategoryId == dto.MacroCategoryId
                && o.FeatureId == dto.FeatureId);

    private static string OverrideKey(OverrideDto dto) =>
        $"plan={dto.PlanTypeId};macro={dto.MacroCategoryId};feature={dto.FeatureId}";
}
