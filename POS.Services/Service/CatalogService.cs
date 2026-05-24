using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Memory;
using POS.Domain.DTOs.Catalogs;
using POS.Domain.DTOs.Tax;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Read-only service that surfaces system catalogs to the public API.
/// Every endpoint goes through a uniform cache-aside helper that
/// projects entities to DTOs, sorts deterministically (BDD-021 §6.1.E),
/// computes an SHA-256 strong ETag, and stores the resulting
/// <see cref="CatalogResponse{T}"/> envelope in <see cref="IMemoryCache"/>
/// for one hour. A per-key <see cref="SemaphoreSlim"/> serializes
/// rebuilds to prevent thundering-herd database hits on first miss.
/// </summary>
public class CatalogService : ICatalogService
{
    #region Constants and Static State

    /// <summary>Cache key prefix shared by every catalog entry — see BDD-021 §6.1.D.</summary>
    private const string CacheKeyPrefix = "Catalog::";

    /// <summary>Uniform absolute cache lifetime — see BDD-021 §7.3.</summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    /// <summary>
    /// Stampede-protection map. Static lifetime is load-bearing because the
    /// service itself is registered Scoped: a per-instance dictionary would
    /// be empty on every concurrent caller and leave the rebuild path
    /// unprotected. With ≤ ~14 keys and semaphores never disposed, the
    /// footprint is bounded — see BDD-021 Appendix C.1.
    /// </summary>
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks = new();

    /// <summary>
    /// Tracks every cache key the service has ever populated so
    /// <see cref="Invalidate"/> can clear matching entries without an
    /// "enumerate-keys" API on <see cref="IMemoryCache"/>.
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> KnownCacheKeys = new();

    /// <summary>
    /// Deterministic <see cref="JsonSerializerOptions"/> used to compute
    /// the SHA-256 fingerprint. Mirrors the global API serializer (see
    /// Program.cs) so the hashed bytes are byte-for-byte identical to what
    /// the controller writes on the wire.
    /// </summary>
    private static readonly JsonSerializerOptions FingerprintOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        Converters = { new JsonStringEnumConverter() }
    };

    #endregion

    #region Fields and Constructor

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;

    public CatalogService(IUnitOfWork unitOfWork, IMemoryCache cache)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    #endregion

    #region Simple Catalog Endpoints

    /// <inheritdoc />
    public Task<CatalogResponse<KitchenStatusDto>> GetKitchenStatusesAsync() =>
        GetOrCreateEnvelopeAsync(
            "KitchenStatuses",
            async () =>
            {
                var rows = await _unitOfWork.Catalog.GetKitchenStatusesAsync();
                return rows
                    .Select(x => new KitchenStatusDto(x.Id, x.Code, x.Name, x.Color, x.SortOrder))
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .ToList();
            });

    /// <inheritdoc />
    public Task<CatalogResponse<DisplayStatusDto>> GetDisplayStatusesAsync() =>
        GetOrCreateEnvelopeAsync(
            "DisplayStatuses",
            async () =>
            {
                var rows = await _unitOfWork.Catalog.GetDisplayStatusesAsync();
                return rows
                    .Select(x => new DisplayStatusDto(x.Id, x.Code, x.Name, x.Color, x.SortOrder))
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .ToList();
            });

    /// <inheritdoc />
    public Task<CatalogResponse<PaymentMethodDto>> GetPaymentMethodsAsync() =>
        GetOrCreateEnvelopeAsync(
            "PaymentMethods",
            async () =>
            {
                var rows = await _unitOfWork.Catalog.GetPaymentMethodsAsync();
                return rows
                    .Select(x => new PaymentMethodDto(x.Id, x.Code, x.Name, x.SortOrder))
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Code, StringComparer.Ordinal)
                    .ToList();
            });

    /// <inheritdoc />
    public Task<CatalogResponse<DeviceModeDto>> GetDeviceModesAsync() =>
        GetOrCreateEnvelopeAsync(
            "DeviceModes",
            async () =>
            {
                var rows = await _unitOfWork.Catalog.GetDeviceModesAsync();
                return rows
                    .Select(x => new DeviceModeDto(x.Id, x.Code, x.Name, x.Description))
                    .OrderBy(x => x.Code, StringComparer.Ordinal)
                    .ToList();
            });

    /// <inheritdoc />
    public Task<CatalogResponse<BusinessTypeDto>> GetBusinessTypesAsync() =>
        GetOrCreateEnvelopeAsync(
            "BusinessTypes",
            async () =>
            {
                var rows = await _unitOfWork.Catalog.GetBusinessTypesAsync();
                return rows
                    .Select(x => new BusinessTypeDto(x.Id, x.PrimaryMacroCategoryId, x.Name))
                    .OrderBy(x => x.PrimaryMacroCategoryId)
                    .ThenBy(x => x.Name, StringComparer.Ordinal)
                    .ToList();
            });

    /// <inheritdoc />
    public Task<CatalogResponse<MacroCategoryDto>> GetMacroCategoriesAsync() =>
        GetOrCreateEnvelopeAsync(
            "MacroCategories",
            async () =>
            {
                var rows = await _unitOfWork.Catalog.GetMacroCategoriesAsync();
                return rows
                    .Select(x => new MacroCategoryDto(
                        x.Id,
                        x.InternalCode,
                        x.PublicName,
                        x.Description,
                        x.PosExperience,
                        x.HasKitchen,
                        x.HasTables))
                    .OrderBy(x => x.Id)
                    .ToList();
            });

    /// <inheritdoc />
    public Task<CatalogResponse<ZoneTypeDto>> GetZoneTypesAsync() =>
        GetOrCreateEnvelopeAsync(
            "ZoneTypes",
            async () =>
            {
                var rows = await _unitOfWork.Catalog.GetZoneTypesAsync();
                return rows
                    .Select(x => new ZoneTypeDto(x.Id, x.Code, x.Name, x.SortOrder))
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Code, StringComparer.Ordinal)
                    .ToList();
            });

    /// <inheritdoc />
    public Task<CatalogResponse<PlanTypeDto>> GetPlanTypesAsync() =>
        GetOrCreateEnvelopeAsync(
            "PlanTypes",
            async () =>
            {
                var rows = await _unitOfWork.Catalog.GetPlanTypesAsync();
                return rows
                    .Select(x => new PlanTypeDto(
                        x.Id, x.Code, x.Name, x.SortOrder, x.MonthlyPrice, x.Currency))
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Id)
                    .ToList();
            });

    /// <inheritdoc />
    public Task<CatalogResponse<AccessReasonDto>> GetAccessReasonsAsync() =>
        GetOrCreateEnvelopeAsync(
            "AccessReasons",
            async () =>
            {
                var rows = await _unitOfWork.Catalog.GetAccessReasonsAsync();
                return rows
                    .Select(x => new AccessReasonDto(x.Id, x.Code, x.Name, x.SortOrder))
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Code, StringComparer.Ordinal)
                    .ToList();
            });

    /// <inheritdoc />
    public Task<CatalogResponse<AccessMethodDto>> GetAccessMethodsAsync() =>
        GetOrCreateEnvelopeAsync(
            "AccessMethods",
            async () =>
            {
                var rows = await _unitOfWork.Catalog.GetAccessMethodsAsync();
                return rows
                    .Select(x => new AccessMethodDto(x.Id, x.Code, x.Name, x.SortOrder))
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Code, StringComparer.Ordinal)
                    .ToList();
            });

    #endregion

    #region Composite and Filtered Endpoints

    /// <inheritdoc />
    public Task<CatalogResponse<PlanCatalogDto>> GetPlanCatalogAsync() =>
        GetOrCreateEnvelopeAsync("Plans", BuildPlanCatalogAsync);

    /// <inheritdoc />
    public Task<CatalogResponse<TaxDto>> GetTaxCatalogAsync(string? countryCode = null)
    {
        // Per-(resource, countryCode) cache key so each filter combination
        // gets its own pre-hashed envelope. With the current MX-only seed,
        // only two keys are ever populated ("ALL" and "MX").
        var normalized = string.IsNullOrWhiteSpace(countryCode)
            ? null
            : countryCode.Trim().ToUpperInvariant();

        var resourceName = normalized is null
            ? "Taxes::ALL"
            : $"Taxes::{normalized}";

        return GetOrCreateEnvelopeAsync(
            resourceName,
            async () =>
            {
                var rows = normalized is null
                    ? await _unitOfWork.Taxes.GetAsync()
                    : await _unitOfWork.Taxes.GetAsync(t => t.CountryCode == normalized);

                return rows
                    .Select(t => new TaxDto(t.Id, t.Code, t.CountryCode, t.IsDefault, t.Name, t.Rate))
                    .OrderBy(t => t.CountryCode, StringComparer.Ordinal)
                    .ThenByDescending(t => t.IsDefault)
                    .ThenBy(t => t.Rate)
                    .ToList();
            });
    }

    #endregion

    #region Cache Invalidation

    /// <inheritdoc />
    public void Invalidate(string? resourceName = null)
    {
        if (resourceName is null)
        {
            foreach (var key in KnownCacheKeys.Keys.ToArray())
            {
                _cache.Remove(key);
                KnownCacheKeys.TryRemove(key, out _);
            }
            return;
        }

        var fullKey = CacheKeyPrefix + resourceName;
        _cache.Remove(fullKey);
        KnownCacheKeys.TryRemove(fullKey, out _);
    }

    #endregion

    #region Private Helpers — Cache-Aside, ETag, Plan Catalog Builder

    /// <summary>
    /// Uniform cache-aside helper. Returns the cached envelope on hit;
    /// on miss, acquires the per-key semaphore (preventing thundering-herd
    /// rebuilds — see BDD-021 EC-2), invokes the supplied
    /// <paramref name="builder"/>, fingerprints the payload, stores the
    /// envelope under <see cref="CacheTtl"/>, and returns it.
    /// </summary>
    private async Task<CatalogResponse<TDto>> GetOrCreateEnvelopeAsync<TDto>(
        string resourceName,
        Func<Task<List<TDto>>> builder)
    {
        var cacheKey = CacheKeyPrefix + resourceName;

        if (_cache.TryGetValue(cacheKey, out CatalogResponse<TDto>? cached) && cached is not null)
        {
            return cached;
        }

        var gate = KeyLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Double-checked: a sibling rebuild may have populated the cache
            // while this caller waited on the semaphore.
            if (_cache.TryGetValue(cacheKey, out cached) && cached is not null)
            {
                return cached;
            }

            var payload = await builder().ConfigureAwait(false);
            var etag = ComputeETag(payload);
            var envelope = new CatalogResponse<TDto>(payload, etag);

            _cache.Set(cacheKey, envelope, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheTtl
            });
            KnownCacheKeys[cacheKey] = 0;

            return envelope;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Computes a strong ETag (RFC 9110 §8.8) for a DTO payload by hashing
    /// its canonical JSON serialization with SHA-256 and hex-encoding the
    /// digest. The returned value already includes the surrounding double
    /// quotes required by the standard.
    /// </summary>
    private static string ComputeETag<TDto>(IReadOnlyList<TDto> payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, FingerprintOptions);
        var hash = SHA256.HashData(bytes);
        return "\"" + Convert.ToHexString(hash).ToLowerInvariant() + "\"";
    }

    /// <summary>
    /// Resolves the full Plan × Feature catalog from four repository reads
    /// and folds them into the <see cref="PlanCatalogDto"/> graph. Kept
    /// private because <see cref="GetPlanCatalogAsync"/> wraps it in the
    /// uniform cache-aside helper above.
    /// </summary>
    private async Task<List<PlanCatalogDto>> BuildPlanCatalogAsync()
    {
        var plans = (await _unitOfWork.Catalog.GetPlanTypesAsync()).ToList();
        var features = (await _unitOfWork.Catalog.GetFeatureCatalogsAsync()).ToList();
        var matrix = (await _unitOfWork.Catalog.GetPlanFeatureMatricesAsync()).ToList();
        var applicability = (await _unitOfWork.Catalog.GetBusinessTypeFeaturesAsync()).ToList();

        var featuresById = features.ToDictionary(f => f.Id);

        var applicableMacrosByFeatureId = applicability
            .GroupBy(a => a.FeatureId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<int>)g
                    .Select(a => a.MacroCategoryId)
                    .OrderBy(id => id)
                    .ToList());

        var matrixByPlan = matrix
            .GroupBy(m => m.PlanTypeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<PlanCatalogDto>(plans.Count);

        foreach (var plan in plans.OrderBy(p => p.SortOrder).ThenBy(p => p.Id))
        {
            var featureDtos = new List<PlanCatalogFeatureDto>();

            if (matrixByPlan.TryGetValue(plan.Id, out var planRows))
            {
                foreach (var row in planRows.Where(r => r.IsEnabled))
                {
                    if (!featuresById.TryGetValue(row.FeatureId, out var feature))
                        continue;

                    // Emit the enum name directly so the string is identical
                    // to the JWT `features` claim — see FeatureGateService.
                    var code = feature.Key.ToString();
                    var applicableMacroIds = applicableMacrosByFeatureId.TryGetValue(feature.Id, out var macros)
                        ? macros
                        : (IReadOnlyList<int>)Array.Empty<int>();

                    featureDtos.Add(new PlanCatalogFeatureDto(
                        code,
                        feature.Name,
                        feature.Description,
                        feature.IsQuantitative,
                        feature.ResourceLabel,
                        row.DefaultLimit,
                        applicableMacroIds));
                }
            }

            var sortedFeatures = featureDtos
                .OrderBy(f => f.Code, StringComparer.Ordinal)
                .ToList();

            result.Add(new PlanCatalogDto(
                plan.Id,
                plan.Code,
                plan.Name,
                plan.SortOrder,
                plan.MonthlyPrice,
                plan.Currency,
                sortedFeatures));
        }

        return result;
    }

    #endregion
}
