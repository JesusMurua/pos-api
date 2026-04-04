using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;
using Stripe;
using Stripe.Checkout;

namespace POS.Services.Service;

/// <summary>
/// Handles Stripe Checkout, subscription queries, and cancellations.
/// </summary>
public class StripeService : IStripeService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IStripeClient _stripeClient;

    public StripeService(IUnitOfWork unitOfWork, IStripeClient stripeClient)
    {
        _unitOfWork = unitOfWork;
        _stripeClient = stripeClient;
    }

    #region Public API Methods

    /// <inheritdoc />
    public async Task<string> CreateCheckoutSessionAsync(
        int businessId, string priceId, string successUrl, string cancelUrl)
    {
        var business = await _unitOfWork.Business.GetByIdAsync(businessId)
            ?? throw new NotFoundException($"Business with id {businessId} not found");

        var stripeCustomerId = await GetOrCreateStripeCustomerAsync(business);

        var options = new SessionCreateOptions
        {
            Customer = stripeCustomerId,
            Mode = "subscription",
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = 1
                }
            ],
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = new Dictionary<string, string>
                {
                    { "businessId", businessId.ToString() }
                }
            },
            Metadata = new Dictionary<string, string>
            {
                { "businessId", businessId.ToString() }
            }
        };

        var service = new SessionService(_stripeClient);
        var session = await service.CreateAsync(options);

        return session.Url;
    }

    /// <inheritdoc />
    public async Task<Domain.Models.Subscription?> GetSubscriptionStatusAsync(int businessId)
    {
        return await _unitOfWork.Subscriptions.GetByBusinessIdAsync(businessId);
    }

    /// <inheritdoc />
    public async Task CancelSubscriptionAsync(int businessId)
    {
        var subscription = await _unitOfWork.Subscriptions.GetByBusinessIdAsync(businessId)
            ?? throw new NotFoundException($"No active subscription found for business {businessId}");

        var stripeSubService = new Stripe.SubscriptionService(_stripeClient);
        await stripeSubService.UpdateAsync(subscription.StripeSubscriptionId, new SubscriptionUpdateOptions
        {
            CancelAtPeriodEnd = true
        });

        subscription.Status = StripeSubscriptionStatus.Canceled;
        subscription.CanceledAt = DateTime.UtcNow;
        _unitOfWork.Subscriptions.Update(subscription);
        await _unitOfWork.SaveChangesAsync();
    }

    #endregion

    #region Private Helper Methods

    private async Task<string> GetOrCreateStripeCustomerAsync(Business business)
    {
        var existingSubscription = await _unitOfWork.Subscriptions.GetByBusinessIdAsync(business.Id);
        if (existingSubscription != null)
            return existingSubscription.StripeCustomerId;

        var stripeCustomerService = new Stripe.CustomerService(_stripeClient);
        var customer = await stripeCustomerService.CreateAsync(new CustomerCreateOptions
        {
            Name = business.Name,
            Metadata = new Dictionary<string, string>
            {
                { "businessId", business.Id.ToString() }
            }
        });

        return customer.Id;
    }

    #endregion
}
