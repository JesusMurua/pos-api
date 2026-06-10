using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class SubscriptionRepository : GenericRepository<Subscription>, ISubscriptionRepository
{
    public SubscriptionRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Subscription?> GetByBusinessIdAsync(int businessId)
    {
        return await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.BusinessId == businessId);
    }

    public async Task<Subscription?> GetByStripeCustomerIdAsync(string stripeCustomerId)
    {
        return await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeCustomerId == stripeCustomerId);
    }

    public async Task<Subscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId)
    {
        return await _context.Subscriptions
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId);
    }

    public async Task<Subscription?> GetByStripeSubscriptionIdWithAddOnsAsync(string stripeSubscriptionId)
    {
        // Eager-load AddOns so the worker can upsert/deactivate them in memory. Only the
        // ACTIVE add-ons are needed for the sync (deactivated rows are immutable history).
        return await _context.Subscriptions
            .Include(s => s.AddOns.Where(a => a.DeactivatedAt == null))
            .FirstOrDefaultAsync(s => s.StripeSubscriptionId == stripeSubscriptionId);
    }
}
