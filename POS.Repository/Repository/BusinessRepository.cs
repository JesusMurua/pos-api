using Microsoft.EntityFrameworkCore;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class BusinessRepository : GenericRepository<Business>, IBusinessRepository
{
    public BusinessRepository(ApplicationDbContext context) : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<Business> Items, int Total)> GetAllForAdminAsync(
        int pageNumber,
        int pageSize,
        string? search,
        int? planTypeId = null,
        int? primaryMacroCategoryId = null,
        bool? isActive = null,
        string? trialStatus = null,
        string sortBy = "createdAt",
        string sortDir = "desc",
        CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters() drops the BDD-019 tenant filter so the super-admin
        // surface can list every business regardless of the request's ITenantContext.
        var query = _context.Businesses
            .IgnoreQueryFilters()
            .AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            // .ToLower().Contains is portable between Npgsql (translates to
            // LOWER(...) LIKE ...) and the InMemory test provider (which does
            // not support EF.Functions.ILike). Acceptable for the current
            // tenant volume; revisit with an expression index if the directory
            // exceeds ~100k rows.
            var needle = search.ToLower();
            query = query.Where(b =>
                b.Name.ToLower().Contains(needle) ||
                b.Users!.Any(u => u.RoleId == UserRoleIds.Owner &&
                                   u.Email != null &&
                                   u.Email.ToLower().Contains(needle)));
        }

        if (planTypeId.HasValue)
            query = query.Where(b => b.PlanTypeId == planTypeId.Value);

        if (primaryMacroCategoryId.HasValue)
            query = query.Where(b => b.PrimaryMacroCategoryId == primaryMacroCategoryId.Value);

        if (isActive.HasValue)
            query = query.Where(b => b.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(trialStatus))
        {
            var nowUtc = DateTime.UtcNow;
            // The trial-status filter classifies businesses against TrialEndsAt
            // relative to "now". A null TrialEndsAt excludes the row from every
            // bucket — those tenants are Free plan or never had a trial assigned.
            query = trialStatus.ToLowerInvariant() switch
            {
                "active" => query.Where(b => b.TrialEndsAt != null && b.TrialEndsAt > nowUtc),
                "expiring-7d" => query.Where(b => b.TrialEndsAt != null
                                                   && b.TrialEndsAt > nowUtc
                                                   && b.TrialEndsAt <= nowUtc.AddDays(7)),
                "expiring-14d" => query.Where(b => b.TrialEndsAt != null
                                                    && b.TrialEndsAt > nowUtc
                                                    && b.TrialEndsAt <= nowUtc.AddDays(14)),
                "expired" => query.Where(b => b.TrialEndsAt != null && b.TrialEndsAt <= nowUtc),
                _ => query
            };
        }

        var total = await query.CountAsync(cancellationToken);

        // Sort dispatch. The lastLoginAt sort uses Max(LastLoginAt) over the
        // Owner users so a tenant with no Owner-with-login lands at the
        // ascending tail; descending pushes it to the head. Branch-scoped
        // sorts could be added later — for now the three the FE asked for.
        var descending = !string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        IOrderedQueryable<Business> ordered = (sortBy?.ToLowerInvariant()) switch
        {
            "name" => descending
                ? query.OrderByDescending(b => b.Name)
                : query.OrderBy(b => b.Name),
            "lastloginat" => descending
                ? query.OrderByDescending(b => b.Users!
                    .Where(u => u.RoleId == UserRoleIds.Owner)
                    .Max(u => (DateTime?)u.LastLoginAt))
                : query.OrderBy(b => b.Users!
                    .Where(u => u.RoleId == UserRoleIds.Owner)
                    .Max(u => (DateTime?)u.LastLoginAt)),
            _ => descending
                ? query.OrderByDescending(b => b.CreatedAt)
                : query.OrderBy(b => b.CreatedAt)
        };

        var skip = Math.Max(0, (pageNumber - 1) * pageSize);

        var items = await ordered
            .ThenByDescending(b => b.Id)
            .Skip(skip)
            .Take(pageSize)
            // Filtered Include + Take limits the materialized owner set to at
            // most one row per business — avoids dragging the entire Users
            // collection for tenants with many staff users.
            .Include(b => b.Users!
                .Where(u => u.RoleId == UserRoleIds.Owner)
                .OrderBy(u => u.CreatedAt)
                .Take(1))
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    /// <inheritdoc />
    public async Task<Business?> GetByIdForAdminAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Businesses
            .IgnoreQueryFilters()
            // Tracked load (not AsNoTracking) so the caller can mutate the
            // result inside the same request — status, plan, and trial
            // PATCH actions write through the returned entity.
            .Include(b => b.Branches)
            .Include(b => b.Users!
                .Where(u => u.RoleId == UserRoleIds.Owner)
                .OrderBy(u => u.CreatedAt)
                .Take(1))
            .Include(b => b.BusinessGiros)
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AdminBusinessStatsRaw> GetAdminStatsAsync(
        DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        // Sequential rather than Task.WhenAll because every query shares the
        // scoped ApplicationDbContext — concurrent operations on the same
        // context throw "second operation started before previous completed".
        // For < ~100k businesses every individual count is sub-10ms; total
        // wall-clock stays well under 200ms without parallelism.

        var businesses = _context.Businesses.IgnoreQueryFilters();

        var totalBusinesses = await businesses.CountAsync(cancellationToken);
        var activeBusinesses = await businesses
            .Where(b => b.IsActive)
            .CountAsync(cancellationToken);
        var inactiveBusinesses = totalBusinesses - activeBusinesses;

        var countsByPlanTypeId = await businesses
            .GroupBy(b => b.PlanTypeId)
            .Select(g => new { PlanTypeId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.PlanTypeId, x => x.Count, cancellationToken);

        var countsByMacroId = await businesses
            .GroupBy(b => b.PrimaryMacroCategoryId)
            .Select(g => new { MacroId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.MacroId, x => x.Count, cancellationToken);

        // Half-open window (NOW, NOW + Nd]: trials already past now are
        // excluded, trials exactly at now are excluded, and trials ending
        // exactly Nd from now are included.
        var sevenDaysFromNow = nowUtc.AddDays(7);
        var fourteenDaysFromNow = nowUtc.AddDays(14);

        var trialsExpiring7Days = await businesses
            .Where(b => b.TrialEndsAt != null
                        && b.TrialEndsAt > nowUtc
                        && b.TrialEndsAt <= sevenDaysFromNow)
            .CountAsync(cancellationToken);

        var trialsExpiring14Days = await businesses
            .Where(b => b.TrialEndsAt != null
                        && b.TrialEndsAt > nowUtc
                        && b.TrialEndsAt <= fourteenDaysFromNow)
            .CountAsync(cancellationToken);

        var onboardingCompleted = await businesses
            .Where(b => b.OnboardingCompleted)
            .CountAsync(cancellationToken);

        var onboardingPending = totalBusinesses - onboardingCompleted;

        var totalUsers = await _context.Users
            .IgnoreQueryFilters()
            .CountAsync(cancellationToken);

        var totalProducts = await _context.Products
            .IgnoreQueryFilters()
            .CountAsync(cancellationToken);

        // Last 6 calendar months including the current one. Threshold
        // computed against the first day of the bucket five months back
        // so any CreatedAt within those buckets is included.
        var currentMonthStart = new DateTime(
            nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var sixMonthThreshold = currentMonthStart.AddMonths(-5);

        var rawByYearMonth = await businesses
            .Where(b => b.CreatedAt >= sixMonthThreshold)
            .GroupBy(b => new { b.CreatedAt.Year, b.CreatedAt.Month })
            .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var countsByYearMonth = rawByYearMonth
            .ToDictionary(x => (x.Year, x.Month), x => x.Count);

        return new AdminBusinessStatsRaw(
            TotalBusinesses: totalBusinesses,
            ActiveBusinesses: activeBusinesses,
            InactiveBusinesses: inactiveBusinesses,
            CountsByPlanTypeId: countsByPlanTypeId,
            CountsByMacroId: countsByMacroId,
            TrialsExpiring7Days: trialsExpiring7Days,
            TrialsExpiring14Days: trialsExpiring14Days,
            OnboardingCompleted: onboardingCompleted,
            OnboardingPending: onboardingPending,
            TotalUsers: totalUsers,
            TotalProducts: totalProducts,
            CountsByYearMonth: countsByYearMonth);
    }
}
