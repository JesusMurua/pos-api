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

    public async Task<IEnumerable<Order>> GetPullOrdersAsync(int branchId, DateTime since)
    {
        return await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                && o.CancellationReason == null
                && (o.UpdatedAt > since || o.CreatedAt > since))
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .OrderByDescending(o => o.UpdatedAt ?? o.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetActiveDeliveryOrdersAsync(int branchId)
    {
        return await _context.Orders
            .Where(o => o.BranchId == branchId
                && o.OrderSource != OrderSource.Direct
                && o.DeliveryStatus != DeliveryStatus.PickedUp
                && o.DeliveryStatus != DeliveryStatus.Rejected)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<Order?> GetByExternalIdAsync(int branchId, string externalOrderId)
    {
        return await _context.Orders
            .FirstOrDefaultAsync(o => o.BranchId == branchId
                && o.ExternalOrderId == externalOrderId);
    }
}
