using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class SubscriptionItemRepository : GenericRepository<SubscriptionItem>, ISubscriptionItemRepository
{
    public SubscriptionItemRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<int> SumAddonQuantityByFeatureAsync(int businessId, FeatureKey featureKey)
    {
        // Resolve the in-memory catalog of Price IDs that map to this feature
        // BEFORE hitting the DB — EF Core cannot translate dictionary lookups
        // into SQL, so we materialize the list and use Contains (translates to IN).
        var targetPriceIds = StripeConstants.AddonPriceMap
            .Where(kvp => kvp.Value.Feature == featureKey)
            .Select(kvp => kvp.Key)
            .ToList();

        if (targetPriceIds.Count == 0) return 0;

        return await _context.SubscriptionItems
            .AsNoTracking()
            .Where(i => i.Subscription.BusinessId == businessId
                     && (i.Subscription.Status == StripeSubscriptionStatus.Active
                      || i.Subscription.Status == StripeSubscriptionStatus.Trialing)
                     && !i.IsBasePlan
                     && targetPriceIds.Contains(i.StripePriceId))
            .SumAsync(i => i.Quantity);
    }
}
