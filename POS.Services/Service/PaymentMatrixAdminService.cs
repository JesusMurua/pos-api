using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using POS.Domain.DTOs.Admin;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Domain.Models.Catalogs;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class PaymentMatrixAdminService : IPaymentMatrixAdminService
{
    private readonly ApplicationDbContext _context;
    private readonly ICatalogService _catalogService;
    private readonly PaymentMethodCacheGeneration _generation;

    private static readonly HashSet<int> ValidPlanIds = new() { 1, 2, 3, 4 };

    public PaymentMatrixAdminService(
        ApplicationDbContext context, ICatalogService catalogService, PaymentMethodCacheGeneration generation)
    {
        _context = context;
        _catalogService = catalogService;
        _generation = generation;
    }

    // ── Catalog ───────────────────────────────────────────────────────

    public async Task<IReadOnlyList<PaymentMethodCatalogDto>> GetCatalogAsync() =>
        await _context.PaymentMethodCatalogs.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Select(x => ToDto(x)).ToListAsync();

    public async Task<PaymentMethodCatalogDto> CreateCatalogAsync(UpsertPaymentMethodCatalogRequest request, string? tokenId)
    {
        ValidateSat(request.SatPaymentFormCode);
        if (await _context.PaymentMethodCatalogs.AnyAsync(c => c.Code == request.Code))
            throw new ValidationException($"A payment method with code '{request.Code}' already exists.");

        var row = new PaymentMethodCatalog
        {
            Code = request.Code,
            Name = request.Name,
            SortOrder = request.SortOrder,
            Category = request.Category,
            SatPaymentFormCode = request.SatPaymentFormCode,
            RequiresReference = request.RequiresReference,
            RequiresCustomer = request.RequiresCustomer,
            SupportsOverpay = request.SupportsOverpay,
            SupportsPartial = request.SupportsPartial,
            ProviderKey = request.ProviderKey,
            CountryCode = request.CountryCode,
            IconClass = request.IconClass,
            IsActive = request.IsActive,
            IsSystem = false
        };
        _context.PaymentMethodCatalogs.Add(row);
        AddAudit("catalog", $"method={row.Code}", null, Json(row), tokenId);
        await SaveAndInvalidateAsync();
        return ToDto(row);
    }

    public async Task UpdateCatalogAsync(int id, UpsertPaymentMethodCatalogRequest request, string? tokenId)
    {
        ValidateSat(request.SatPaymentFormCode);
        var row = await _context.PaymentMethodCatalogs.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException($"Payment method {id} not found.");

        var before = Json(row);
        // Code is immutable (it's the freeze key); metadata is editable.
        row.Name = request.Name;
        row.SortOrder = request.SortOrder;
        row.Category = request.Category;
        row.SatPaymentFormCode = request.SatPaymentFormCode;
        row.RequiresReference = request.RequiresReference;
        row.RequiresCustomer = request.RequiresCustomer;
        row.SupportsOverpay = request.SupportsOverpay;
        row.SupportsPartial = request.SupportsPartial;
        row.ProviderKey = request.ProviderKey;
        row.CountryCode = request.CountryCode;
        row.IconClass = request.IconClass;
        row.IsActive = request.IsActive;

        AddAudit("catalog", $"method={row.Code}", before, Json(row), tokenId);
        await SaveAndInvalidateAsync();
    }

    public async Task DeleteCatalogAsync(int id, string? tokenId)
    {
        var row = await _context.PaymentMethodCatalogs.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException($"Payment method {id} not found.");

        if (row.IsSystem)
            throw new ConcurrencyConflictException("System payment methods cannot be deleted.");

        var hasPayments = await _context.OrderPayments.IgnoreQueryFilters()
            .AnyAsync(p => p.PaymentMethodId == id);
        var before = Json(row);

        if (hasPayments)
        {
            // Soft-delete: preserves history and the RESTRICT FK.
            row.IsActive = false;
            AddAudit("catalog", $"method={row.Code};soft-delete", before, Json(row), tokenId);
        }
        else
        {
            _context.PaymentMethodCatalogs.Remove(row);
            AddAudit("catalog", $"method={row.Code};delete", before, null, tokenId);
        }
        await SaveAndInvalidateAsync();
    }

    // ── Plan matrix ───────────────────────────────────────────────────

    public async Task<IReadOnlyList<PlanPaymentMethodEntryDto>> GetPlanMatrixAsync() =>
        await _context.PlanPaymentMethodMatrices.AsNoTracking()
            .Select(m => new PlanPaymentMethodEntryDto(m.PlanTypeId, m.PaymentMethodId, m.IsEnabled))
            .ToListAsync();

    public async Task BulkUpsertPlanMatrixAsync(IReadOnlyList<PlanPaymentMethodEntryDto> entries, string? tokenId)
    {
        var validMethodIds = await ValidMethodIdsAsync();
        foreach (var e in entries)
        {
            if (!ValidPlanIds.Contains(e.PlanTypeId))
                throw new ValidationException($"Unknown planTypeId {e.PlanTypeId}");
            if (!validMethodIds.Contains(e.PaymentMethodId))
                throw new ValidationException($"Unknown paymentMethodId {e.PaymentMethodId}");
        }

        var existing = await _context.PlanPaymentMethodMatrices.ToListAsync();
        var byKey = existing.ToDictionary(m => (m.PlanTypeId, m.PaymentMethodId));

        foreach (var e in entries)
        {
            if (byKey.TryGetValue((e.PlanTypeId, e.PaymentMethodId), out var row))
            {
                if (row.IsEnabled == e.IsEnabled) continue;
                var before = Json(new { row.PlanTypeId, row.PaymentMethodId, row.IsEnabled });
                row.IsEnabled = e.IsEnabled;
                AddAudit("plan", $"plan={e.PlanTypeId};method={e.PaymentMethodId}", before,
                    Json(new { e.PlanTypeId, e.PaymentMethodId, e.IsEnabled }), tokenId);
            }
            else
            {
                _context.PlanPaymentMethodMatrices.Add(new PlanPaymentMethodMatrix
                {
                    PlanTypeId = e.PlanTypeId,
                    PaymentMethodId = e.PaymentMethodId,
                    IsEnabled = e.IsEnabled
                });
                AddAudit("plan", $"plan={e.PlanTypeId};method={e.PaymentMethodId}", null,
                    Json(new { e.PlanTypeId, e.PaymentMethodId, e.IsEnabled }), tokenId);
            }
        }
        await SaveAndInvalidateAsync();
    }

    // ── Tenant overrides ──────────────────────────────────────────────

    public async Task<IReadOnlyList<TenantPaymentMethodOverrideDto>> GetOverridesAsync() =>
        await _context.TenantPaymentMethodOverrides.AsNoTracking()
            .Select(o => new TenantPaymentMethodOverrideDto(
                o.Id, o.BusinessId, o.PaymentMethodId, o.IsEnabled, o.CustomLabel, o.ProviderConfigJson))
            .ToListAsync();

    public async Task<TenantPaymentMethodOverrideDto> CreateOverrideAsync(CreateTenantOverrideRequest request, string? tokenId)
    {
        if (!await _context.Businesses.IgnoreQueryFilters().AnyAsync(b => b.Id == request.BusinessId))
            throw new ValidationException($"Business {request.BusinessId} not found.");
        if (!(await ValidMethodIdsAsync()).Contains(request.PaymentMethodId))
            throw new ValidationException($"Unknown paymentMethodId {request.PaymentMethodId}");
        if (await _context.TenantPaymentMethodOverrides.AnyAsync(
                o => o.BusinessId == request.BusinessId && o.PaymentMethodId == request.PaymentMethodId))
            throw new ValidationException("An override for this business and method already exists.");

        var row = new TenantPaymentMethodOverride
        {
            BusinessId = request.BusinessId,
            PaymentMethodId = request.PaymentMethodId,
            IsEnabled = request.IsEnabled,
            CustomLabel = request.CustomLabel,
            ProviderConfigJson = request.ProviderConfigJson
        };
        _context.TenantPaymentMethodOverrides.Add(row);
        AddAudit("override", $"business={row.BusinessId};method={row.PaymentMethodId}", null,
            Json(new { row.BusinessId, row.PaymentMethodId, row.IsEnabled }), tokenId);
        await SaveAndInvalidateAsync();
        return new TenantPaymentMethodOverrideDto(
            row.Id, row.BusinessId, row.PaymentMethodId, row.IsEnabled, row.CustomLabel, row.ProviderConfigJson);
    }

    public async Task UpdateOverrideAsync(int id, UpdateTenantOverrideRequest request, string? tokenId)
    {
        var row = await _context.TenantPaymentMethodOverrides.FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new NotFoundException($"Override {id} not found.");

        var before = Json(new { row.BusinessId, row.PaymentMethodId, row.IsEnabled });
        row.IsEnabled = request.IsEnabled;
        row.CustomLabel = request.CustomLabel;
        row.ProviderConfigJson = request.ProviderConfigJson;
        AddAudit("override", $"business={row.BusinessId};method={row.PaymentMethodId}", before,
            Json(new { row.BusinessId, row.PaymentMethodId, row.IsEnabled }), tokenId);
        await SaveAndInvalidateAsync();
    }

    public async Task DeleteOverrideAsync(int id, string? tokenId)
    {
        var row = await _context.TenantPaymentMethodOverrides.FirstOrDefaultAsync(o => o.Id == id)
            ?? throw new NotFoundException($"Override {id} not found.");

        AddAudit("override", $"business={row.BusinessId};method={row.PaymentMethodId};delete",
            Json(new { row.BusinessId, row.PaymentMethodId, row.IsEnabled }), null, tokenId);
        _context.TenantPaymentMethodOverrides.Remove(row);
        await SaveAndInvalidateAsync();
    }

    // ── Preview impact ────────────────────────────────────────────────

    public async Task<PaymentPreviewImpactDto> PreviewImpactAsync(int paymentMethodId, int planTypeId, bool enabled)
    {
        var shielded = await _context.TenantPaymentMethodOverrides
            .Where(o => o.PaymentMethodId == paymentMethodId)
            .Select(o => o.BusinessId).ToListAsync();

        var planName = await _context.PlanTypeCatalogs
            .Where(p => p.Id == planTypeId).Select(p => p.Name).FirstOrDefaultAsync() ?? planTypeId.ToString();

        var affected = await _context.Businesses.IgnoreQueryFilters()
            .Where(b => b.PlanTypeId == planTypeId && !shielded.Contains(b.Id))
            .Select(b => new AffectedTenantDto(b.Id, b.Name, planName))
            .ToListAsync();

        return new PaymentPreviewImpactDto(affected.Count, affected);
    }

    // ── Audit log ─────────────────────────────────────────────────────

    public async Task<PagedPaymentAuditLogDto> GetAuditLogAsync(
        DateTime? from, DateTime? to, string? axis, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 50;

        var query = _context.Set<PaymentMatrixAuditLog>().AsNoTracking().AsQueryable();
        if (from.HasValue) query = query.Where(a => a.ChangedAt >= from.Value);
        if (to.HasValue) query = query.Where(a => a.ChangedAt < to.Value);
        if (!string.IsNullOrWhiteSpace(axis)) query = query.Where(a => a.Axis == axis);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.ChangedAt).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new PaymentMatrixAuditEntryDto(
                a.Id, a.ChangedAt, a.ChangedByTokenId, a.Axis, a.EntityKey, a.BeforeJson, a.AfterJson))
            .ToListAsync();

        return new PagedPaymentAuditLogDto(page, pageSize, total, items);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static void ValidateSat(string code)
    {
        if (!SatPaymentFormCodes.IsValid(code))
            throw new ValidationException($"Invalid SAT payment form code '{code}'. Must be a SAT c_FormaPago value.");
    }

    private async Task<HashSet<int>> ValidMethodIdsAsync() =>
        (await _context.PaymentMethodCatalogs.AsNoTracking().Select(c => c.Id).ToListAsync()).ToHashSet();

    private void AddAudit(string axis, string entityKey, string? before, string? after, string? tokenId)
    {
        _context.Set<PaymentMatrixAuditLog>().Add(new PaymentMatrixAuditLog
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
        _generation.Bump();                          // public /available per-tenant cache
        _catalogService.Invalidate("PaymentMethods"); // anonymous raw-catalog envelope
    }

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static PaymentMethodCatalogDto ToDto(PaymentMethodCatalog x) => new(
        x.Id, x.Code, x.Name, x.SortOrder, x.Category, x.SatPaymentFormCode,
        x.RequiresReference, x.RequiresCustomer, x.SupportsOverpay, x.SupportsPartial,
        x.ProviderKey, x.CountryCode, x.IconClass, x.IsActive, x.IsSystem);
}
