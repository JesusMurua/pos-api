using Microsoft.EntityFrameworkCore;
using POS.Domain.DTOs.Admin;
using POS.Domain.Enums;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class AdminAuditLogService : IAdminAuditLogService
{
    private readonly ApplicationDbContext _context;

    public AdminAuditLogService(ApplicationDbContext context) => _context = context;

    public Task<PagedBusinessAuditLogDto> GetForBusinessAsync(
        int businessId, string? action, DateTime? from, DateTime? to, int page, int pageSize) =>
        QueryAsync(businessId, action, from, to, page, pageSize);

    public Task<PagedBusinessAuditLogDto> GetCrossTenantAsync(
        int? businessId, string? action, DateTime? from, DateTime? to, int page, int pageSize) =>
        QueryAsync(businessId, action, from, to, page, pageSize);

    private async Task<PagedBusinessAuditLogDto> QueryAsync(
        int? businessId, string? action, DateTime? from, DateTime? to, int page, int pageSize)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

        // Cross-tenant admin read: the log is operator-owned, never tenant-scoped.
        var query = _context.BusinessAuditLogs.AsNoTracking().IgnoreQueryFilters();
        if (businessId.HasValue) query = query.Where(a => a.BusinessId == businessId.Value);
        if (from.HasValue) query = query.Where(a => a.ChangedAtUtc >= from.Value);
        if (to.HasValue) query = query.Where(a => a.ChangedAtUtc <= to.Value);
        if (!string.IsNullOrWhiteSpace(action))
        {
            // Typed filter: an unmatched action name yields zero rows rather than a silent
            // "all rows" — the caller is told nothing matched, not given the wrong page.
            query = Enum.TryParse<BusinessAuditAction>(action, ignoreCase: true, out var parsed)
                ? query.Where(a => a.Action == parsed)
                : query.Where(_ => false);
        }

        var total = await query.CountAsync();

        // Project to an anonymous shape first; Action carries a HasConversion<string> so
        // its .ToString() is mapped in memory (not all providers translate it in SQL).
        var rows = await query
            .OrderByDescending(a => a.ChangedAtUtc).ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new
            {
                a.Id, a.BusinessId, a.Action, a.ChangedAtUtc,
                a.ChangedByTokenId, a.Reason, a.BeforeJson, a.AfterJson
            })
            .ToListAsync();

        var items = rows.Select(a => new BusinessAuditEntryDto(
            a.Id, a.BusinessId, a.Action.ToString(), a.ChangedAtUtc,
            a.ChangedByTokenId, a.Reason, a.BeforeJson, a.AfterJson)).ToList();

        return new PagedBusinessAuditLogDto(page, pageSize, total, items);
    }
}
