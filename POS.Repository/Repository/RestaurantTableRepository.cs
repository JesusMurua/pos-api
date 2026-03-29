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

    public async Task<IEnumerable<TableStatusProjection>> GetTableStatusProjectionsAsync(int branchId)
    {
        var tables = await _context.RestaurantTables
            .AsNoTracking()
            .Where(t => t.BranchId == branchId && t.IsActive)
            .Select(t => new { t.Id, t.Name, t.ZoneId, ZoneName = t.Zone != null ? t.Zone.Name : "" })
            .ToListAsync();

        var activeOrders = await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId && o.TableId != null
                && o.CancellationReason == null && o.IsPaid == false)
            .Select(o => new { o.TableId, o.Id, o.TotalCents, o.KitchenStatus, o.CreatedAt })
            .ToListAsync();

        var orderByTable = activeOrders
            .GroupBy(o => o.TableId)
            .ToDictionary(g => g.Key!.Value, g => g.OrderByDescending(o => o.CreatedAt).First());

        return tables.Select(t =>
        {
            var hasOrder = orderByTable.TryGetValue(t.Id, out var order);
            return new TableStatusProjection
            {
                TableId = t.Id,
                TableName = t.Name,
                ZoneId = t.ZoneId,
                ZoneName = t.ZoneName,
                OrderId = hasOrder ? order!.Id : null,
                OrderTotalCents = hasOrder ? order!.TotalCents : null,
                OrderKitchenStatus = hasOrder ? order!.KitchenStatus : null,
                OrderCreatedAt = hasOrder ? order!.CreatedAt : null
            };
        });
    }
}
