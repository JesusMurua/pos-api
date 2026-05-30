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
}
