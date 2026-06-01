using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IRestaurantTableRepository : IGenericRepository<RestaurantTable>
{
    Task<IEnumerable<RestaurantTable>> GetByBranchAsync(int branchId, bool includeInactive = false);

    Task<RestaurantTable?> GetWithCurrentOrderAsync(int id);

    /// <summary>
    /// Returns lightweight table + active order projections for floor map.
    /// </summary>
    Task<IEnumerable<TableStatusProjection>> GetTableStatusProjectionsAsync(int branchId, string? timezone = null);

    /// <summary>
    /// Counts every restaurant table across every branch of
    /// <paramref name="businessId"/>, bypassing the BDD-019 branch query
    /// filter via <c>IgnoreQueryFilters</c>. Used by
    /// <c>AuthResponse.Snapshot</c> to surface business-wide totals.
    /// </summary>
    Task<int> CountForBusinessAsync(int businessId);
}
