using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IBusinessRepository : IGenericRepository<Business>
{
    /// <summary>
    /// Paginated cross-tenant business directory for the super-admin surface.
    /// Bypasses the global query filters from BDD-019 so the operator sees
    /// every tenant regardless of the <c>ITenantContext</c> on the request,
    /// and eager-loads the first Owner user (<c>RoleId = UserRoleIds.Owner</c>,
    /// oldest by <c>CreatedAt</c>) so callers can render owner contact data
    /// without an N+1 follow-up.
    /// </summary>
    /// <param name="pageNumber">1-based page index.</param>
    /// <param name="pageSize">Rows per page; caller is responsible for clamping.</param>
    /// <param name="search">
    /// Optional case-insensitive substring match against the business name or
    /// the Owner's email. Implemented via <c>.ToLower().Contains(...)</c> so
    /// the same expression tree works on the production Postgres provider and
    /// the integration-test InMemory provider (the latter does not support
    /// <c>EF.Functions.ILike</c>).
    /// </param>
    /// <returns>
    /// A tuple of the page slice (ordered by <c>CreatedAt</c> DESC) and the
    /// unpaginated total count for the filtered query.
    /// </returns>
    Task<(IReadOnlyList<Business> Items, int Total)> GetAllForAdminAsync(
        int pageNumber,
        int pageSize,
        string? search,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Raw cross-tenant aggregate snapshot consumed by
    /// <c>GET /api/Admin/businesses/stats</c>. Every COUNT and GROUP BY
    /// goes through <c>IgnoreQueryFilters</c> so the BDD-019 tenant
    /// filters do not silently clip the totals. The shape stays as raw
    /// counts / dictionaries — the controller is responsible for resolving
    /// <c>PlanTypeIds.ToCode</c> / <c>MacroCategoryIds.ToCode</c> and for
    /// backfilling the six calendar buckets of <see cref="AdminBusinessStatsRaw.CountsByYearMonth"/>.
    /// </summary>
    /// <param name="nowUtc">
    /// Reference instant for trial / growth window calculations. Threading
    /// it through as a parameter keeps the repo deterministic — the caller
    /// (controller) reads <c>DateTime.UtcNow</c> once at the start of the
    /// request so every count in the response uses the same instant.
    /// </param>
    Task<AdminBusinessStatsRaw> GetAdminStatsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}

/// <summary>
/// Repo-layer raw projection of the admin stats endpoint. The controller
/// converts this into the public <c>AdminBusinessStatsResponse</c> by
/// resolving codes and backfilling empty months.
/// </summary>
public sealed record AdminBusinessStatsRaw(
    int TotalBusinesses,
    int ActiveBusinesses,
    int InactiveBusinesses,
    IReadOnlyDictionary<int, int> CountsByPlanTypeId,
    IReadOnlyDictionary<int, int> CountsByMacroId,
    int TrialsExpiring7Days,
    int TrialsExpiring14Days,
    int OnboardingCompleted,
    int OnboardingPending,
    int TotalUsers,
    int TotalProducts,
    IReadOnlyDictionary<(int Year, int Month), int> CountsByYearMonth);
