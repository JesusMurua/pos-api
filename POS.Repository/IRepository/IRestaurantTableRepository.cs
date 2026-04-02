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
}
