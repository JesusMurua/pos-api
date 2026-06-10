using POS.Domain.DTOs.Admin;

namespace POS.Services.IService;

/// <summary>
/// Admin (X-Admin-Token) surface over a tenant's SaaS subscription with remote-first
/// Stripe reconcile (PR-2). Also backs the deprecated PATCH /plan alias. See
/// docs/saas-billing-architecture.md §5/§7.
/// </summary>
public interface IAdminSubscriptionService
{
    /// <summary>Subscription detail + price history. 404 if the business has no subscription.</summary>
    Task<AdminSubscriptionDetailDto> GetAsync(int businessId);

    /// <summary>
    /// Reconcile: applies the requested changes. On the Stripe rail a BaseAmountCents
    /// change creates a dynamic Price and updates Stripe (await 2xx, then persist; on a
    /// Stripe error nothing is committed). Persists Subscription + SubscriptionPriceHistory
    /// + BusinessAuditLog atomically and keeps Business.PlanTypeId in sync.
    /// </summary>
    Task UpdateAsync(int businessId, AdminUpdateSubscriptionRequest request, string? tokenId);

    /// <summary>
    /// Backs the deprecated PATCH /plan alias: sets Business.PlanTypeId (gate SSoT) +
    /// the denormalized Subscription.PlanTypeId if a subscription exists, writes a
    /// PlanChanged audit row, and invalidates the feature cache. Plan-only changes do
    /// not reprice Stripe in v2 (the price-change path owns Stripe reconcile).
    /// </summary>
    Task ChangePlanAsync(int businessId, int planTypeId, string? reason, string? tokenId);

    /// <summary>
    /// Activates an add-on on the business's subscription (PR-4). Remote-first on the Stripe
    /// rail: resolves/creates the add-on Price, appends the Stripe subscription item with
    /// proration, awaits 2xx, then persists the SubscriptionAddOn + audit atomically. Manual
    /// rails persist locally only. Rejects a duplicate active add-on (409) and a price-less
    /// Stripe activation (400).
    /// </summary>
    Task ActivateAddOnAsync(int businessId, AdminActivateAddOnRequest request, string? tokenId);

    /// <summary>
    /// Deactivates an active SubscriptionAddOn (soft, keeps history). On the Stripe rail it
    /// removes the Stripe item (proration) and archives a custom Price post-success. Manual
    /// rails update locally only.
    /// </summary>
    Task DeactivateAddOnAsync(int businessId, int subscriptionAddOnId, string? tokenId);
}
