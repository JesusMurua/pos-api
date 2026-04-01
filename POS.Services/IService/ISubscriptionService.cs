using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides subscription status for the authenticated business.
/// </summary>
public interface ISubscriptionService
{
    /// <summary>
    /// Returns the subscription status for a business.
    /// If no Stripe subscription exists, returns Free plan defaults.
    /// </summary>
    Task<SubscriptionStatusDto> GetStatusAsync(int businessId);
}
