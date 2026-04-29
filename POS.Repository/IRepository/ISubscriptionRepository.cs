using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ISubscriptionRepository : IGenericRepository<Subscription>
{
    Task<Subscription?> GetByBusinessIdAsync(int businessId);
    Task<Subscription?> GetByStripeCustomerIdAsync(string stripeCustomerId);
    Task<Subscription?> GetByStripeSubscriptionIdAsync(string stripeSubscriptionId);

    /// <summary>
    /// Eager-loads the subscription row together with all its
    /// <see cref="SubscriptionItem"/> children. Used by the Stripe webhook
    /// processor to perform a clear-and-replace of the items collection
    /// without leaving orphaned rows in <c>SubscriptionItems</c>.
    /// </summary>
    Task<Subscription?> GetByStripeSubscriptionIdWithItemsAsync(string stripeSubscriptionId);
}
