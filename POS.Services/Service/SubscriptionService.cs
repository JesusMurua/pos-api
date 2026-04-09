using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Provides subscription status for the authenticated business.
/// </summary>
public class SubscriptionService : ISubscriptionService
{
    private readonly IUnitOfWork _unitOfWork;

    public SubscriptionService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<SubscriptionStatusDto> GetStatusAsync(int businessId)
    {
        var subscription = await _unitOfWork.Subscriptions.GetByBusinessIdAsync(businessId);

        if (subscription == null)
        {
            // No Stripe subscription yet — use the PlanTypeId stored on the Business entity
            var business = await _unitOfWork.Business.GetByIdAsync(businessId);

            return new SubscriptionStatusDto
            {
                PlanTypeId = business?.PlanTypeId ?? PlanTypeIds.Free,
                Status = StripeSubscriptionStatus.Active,
                PricingGroup = "General",
                BillingCycle = "Monthly",
                IsActive = true,
                CurrentPeriodEnd = DateTime.UtcNow.AddYears(99)
            };
        }

        return new SubscriptionStatusDto
        {
            PlanTypeId = subscription.PlanTypeId,
            Status = subscription.Status,
            PricingGroup = subscription.PricingGroup,
            BillingCycle = subscription.BillingCycle,
            CurrentPeriodEnd = subscription.CurrentPeriodEnd,
            TrialEndsAt = subscription.TrialEndsAt,
            IsActive = subscription.Status is StripeSubscriptionStatus.Active
                or StripeSubscriptionStatus.Trialing
        };
    }
}
