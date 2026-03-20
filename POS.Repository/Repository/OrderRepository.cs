using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class OrderRepository : GenericRepository<Order>, IOrderRepository
{
    public OrderRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateTime date)
    {
        return await _context.Orders
            .Where(o => o.BranchId == branchId && o.CreatedAt.Date == date.Date)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetPendingSyncAsync()
    {
        return await _context.Orders
            .Where(o => o.SyncStatus == OrderSyncStatus.Pending)
            .Include(o => o.Items)
            .ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateTime date)
    {
        return await _context.Orders
            .Where(o => o.BranchId == branchId
                && o.CreatedAt.Date == date.Date
                && o.SyncStatus != OrderSyncStatus.Failed)
            .Include(o => o.Items)
            .ToListAsync();
    }
}
