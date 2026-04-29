using POS.Domain.Enums;
using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ISubscriptionItemRepository : IGenericRepository<SubscriptionItem>
{
    /// <summary>
    /// Sums the purchased add-on quantities across all <see cref="SubscriptionItem"/>
    /// rows that map to <paramref name="featureKey"/> for the given business.
    /// <para>
    /// Fail-strict policy: only items whose parent <see cref="Subscription.Status"/>
    /// is <c>active</c> or <c>trialing</c> contribute to the sum. Subscriptions in
    /// <c>past_due</c>, <c>unpaid</c>, <c>paused</c>, <c>canceled</c>, or
    /// <c>incomplete</c> states are intentionally excluded — a tenant whose payment
    /// is not current loses access to the add-on capacity until the subscription
    /// recovers.
    /// </para>
    /// </summary>
    Task<int> SumAddonQuantityByFeatureAsync(int businessId, FeatureKey featureKey);
}
