using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class SubscriptionAddOnRepository : GenericRepository<SubscriptionAddOn>, ISubscriptionAddOnRepository
{
    public SubscriptionAddOnRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<int> SumActiveQuantityByLinkAsync(int businessId, FeatureKey featureKey)
    {
        var featureId = (int)featureKey;

        return await _context.SubscriptionAddOns
            .AsNoTracking()
            .Where(sa => sa.Subscription!.BusinessId == businessId
                      && sa.DeactivatedAt == null
                      && (sa.Subscription.Status == StripeSubscriptionStatus.Active
                       || sa.Subscription.Status == StripeSubscriptionStatus.Trialing)
                      && sa.PlanAddOn!.LinkType == PlanAddOnLinkType.DeviceLicense
                      && sa.PlanAddOn.LinkedEntityId == featureId)
            .SumAsync(sa => sa.Quantity);
    }
}
