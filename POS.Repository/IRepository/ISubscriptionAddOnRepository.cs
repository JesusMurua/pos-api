using POS.Domain.Enums;
using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ISubscriptionAddOnRepository : IGenericRepository<SubscriptionAddOn>
{
    /// <summary>
    /// Sums the active add-on quantities that grant <paramref name="featureKey"/> for the
    /// given business (the device-licensing primitive that replaced the retired
    /// <c>SubscriptionItemRepository.SumAddonQuantityByFeatureAsync</c>).
    /// <para>
    /// Fail-strict (CRITICAL — do not relax): only <c>DeactivatedAt IS NULL</c> add-ons whose
    /// parent <see cref="Subscription.Status"/> is <c>active</c> or <c>trialing</c> count. A
    /// tenant in <c>past_due</c>/<c>canceled</c>/<c>paused</c> loses the add-on capacity until
    /// the subscription recovers — relaxing this silently regresses revenue enforcement.
    /// </para>
    /// </summary>
    Task<int> SumActiveQuantityByLinkAsync(int businessId, FeatureKey featureKey);
}
