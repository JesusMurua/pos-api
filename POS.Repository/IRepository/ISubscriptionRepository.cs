using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ISubscriptionRepository : IGenericRepository<Subscription>
{
    Task<Subscription?> GetByBusinessIdAsync(int businessId);
    Task<Subscription?> GetByStripeCustomerIdAsync(string stripeCustomerId);
    Task<Subscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId);

    /// <summary>
    /// Eager-loads the subscription row together with its ACTIVE
    /// <see cref="SubscriptionAddOn"/> children. Used by the Stripe webhook processor to
    /// upsert/deactivate add-ons by <c>StripeItemId</c> (never clear-and-replace — add-ons
    /// carry local-only state that does not live in Stripe).
    /// </summary>
    Task<Subscription?> GetByStripeSubscriptionIdWithAddOnsAsync(string stripeSubscriptionId);
}
