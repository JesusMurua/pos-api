using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class PushSubscriptionRepository : GenericRepository<PushSubscription>, IPushSubscriptionRepository
{
    public PushSubscriptionRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<PushSubscription?> GetByEndpointAsync(string endpoint)
    {
        return await _context.PushSubscriptions
            .FirstOrDefaultAsync(p => p.Endpoint == endpoint);
    }

    public async Task<IEnumerable<PushSubscription>> GetByBranchAsync(int branchId)
    {
        return await _context.PushSubscriptions
            .Where(p => p.BranchId == branchId)
            .ToListAsync();
    }

    public async Task<IEnumerable<PushSubscription>> GetByUserAsync(int userId)
    {
        return await _context.PushSubscriptions
            .Where(p => p.UserId == userId)
            .ToListAsync();
    }
}
