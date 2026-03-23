using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class RestaurantTableRepository : GenericRepository<RestaurantTable>, IRestaurantTableRepository
{
    public RestaurantTableRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<RestaurantTable>> GetByBranchAsync(int branchId, bool includeInactive = false)
    {
        var query = _context.RestaurantTables
            .Where(t => t.BranchId == branchId);

        if (!includeInactive)
            query = query.Where(t => t.IsActive);

        return await query.OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<RestaurantTable?> GetWithCurrentOrderAsync(int id)
    {
        return await _context.RestaurantTables
            .Include(t => t.Orders!.Where(o => o.CancellationReason == null))
            .FirstOrDefaultAsync(t => t.Id == id);
    }
}
