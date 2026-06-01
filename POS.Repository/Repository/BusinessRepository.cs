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

        var total = await query.CountAsync(cancellationToken);

        var skip = Math.Max(0, (pageNumber - 1) * pageSize);

        var items = await query
            .OrderByDescending(b => b.CreatedAt)
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
