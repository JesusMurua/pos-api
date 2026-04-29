using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;
using Stripe;

namespace POS.API.Workers;

/// <summary>
/// Background worker that polls the StripeEventInbox for pending events and processes them sequentially.
/// Implements temporal guards for out-of-order event protection and marks failures for manual inspection.
/// </summary>
public class StripeEventProcessorWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StripeEventProcessorWorker> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 50;

    public StripeEventProcessorWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<StripeEventProcessorWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("StripeEventProcessorWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingEventsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled error in StripeEventProcessorWorker loop");
            }

            await Task.Delay(PollingInterval, stoppingToken);
        }
    }

    private async Task ProcessPendingEventsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var stripeClient = scope.ServiceProvider.GetRequiredService<IStripeClient>();
        var featureGate = scope.ServiceProvider.GetRequiredService<IFeatureGateService>();

        var pendingEvents = await unitOfWork.StripeEventInbox.GetPendingEventsAsync(BatchSize);
        if (pendingEvents.Count == 0) return;

        _logger.LogInformation("Processing {Count} pending Stripe events", pendingEvents.Count);

        foreach (var inboxEvent in pendingEvents)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var stripeEvent = EventUtility.ParseEvent(inboxEvent.RawJson);
                await ProcessEventAsync(stripeEvent, unitOfWork, stripeClient, featureGate);

                inboxEvent.Status = StripeEventStatus.Processed;
                inboxEvent.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                inboxEvent.Status = StripeEventStatus.Failed;
                inboxEvent.ProcessedAt = DateTime.UtcNow;
                inboxEvent.ErrorMessage = ex.Message.Length > 2000
                    ? ex.Message[..2000]
                    : ex.Message;

                _logger.LogError(ex, "Failed to process Stripe event {EventId} ({Type})",
                    inboxEvent.StripeEventId, inboxEvent.Type);
            }

            unitOfWork.StripeEventInbox.Update(inboxEvent);
            await unitOfWork.SaveChangesAsync();
        }
    }

    #region Event Handlers

    private async Task ProcessEventAsync(
        Event stripeEvent,
        IUnitOfWork unitOfWork,
        IStripeClient stripeClient,
        IFeatureGateService featureGate)
    {
        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                await HandleCheckoutSessionCompletedAsync(stripeEvent, unitOfWork, stripeClient, featureGate);
                break;

            case EventTypes.CustomerSubscriptionUpdated:
                await HandleSubscriptionUpdatedAsync(stripeEvent, unitOfWork, featureGate);
                break;

            case EventTypes.CustomerSubscriptionDeleted:
                await HandleSubscriptionDeletedAsync(stripeEvent, unitOfWork, featureGate);
                break;

            case EventTypes.InvoicePaymentFailed:
                await HandleInvoicePaymentFailedAsync(stripeEvent, unitOfWork);
                break;

            case EventTypes.InvoicePaymentSucceeded:
                await HandleInvoicePaymentSucceededAsync(stripeEvent, unitOfWork);
                break;

            default:
                _logger.LogInformation("Unhandled Stripe event type: {Type}", stripeEvent.Type);
                break;
        }
    }

    private async Task HandleCheckoutSessionCompletedAsync(
        Event stripeEvent, IUnitOfWork unitOfWork, IStripeClient stripeClient, IFeatureGateService featureGate)
    {
        var session = stripeEvent.Data.Object as Stripe.Checkout.Session;
        if (session == null) return;

        if (!int.TryParse(session.Metadata?.GetValueOrDefault("businessId"), out var businessId))
        {
            _logger.LogWarning("checkout.session.completed missing businessId metadata");
            return;
        }

        var stripeSubscriptionId = session.SubscriptionId;
        if (string.IsNullOrEmpty(stripeSubscriptionId)) return;

        // Fetch full subscription from Stripe (safe in background — no webhook timeout constraint)
        var subscriptionService = new SubscriptionService(stripeClient);
        var stripeSub = await subscriptionService.GetAsync(stripeSubscriptionId);

        if (stripeSub.Items?.Data == null || stripeSub.Items.Data.Count == 0)
        {
            _logger.LogWarning("checkout.session.completed: subscription {SubId} has no price items",
                stripeSubscriptionId);
            return;
        }

        var firstItem = stripeSub.Items.Data[0];

        // Eager-load with items so the clear-and-replace path operates on the
        // full collection in memory (otherwise EF will not delete orphans).
        var subscription = await unitOfWork.Subscriptions
            .GetByStripeSubscriptionIdWithItemsAsync(stripeSubscriptionId);
        bool isNew = subscription == null;

        if (subscription == null)
        {
            subscription = new Domain.Models.Subscription
            {
                BusinessId = businessId,
                StripeCustomerId = session.CustomerId,
                StripeSubscriptionId = stripeSubscriptionId,
                Status = stripeSub.Status,
                TrialEndsAt = stripeSub.TrialEnd,
                CurrentPeriodStart = firstItem.CurrentPeriodStart,
                CurrentPeriodEnd = firstItem.CurrentPeriodEnd,
                UpdatedAt = DateTime.UtcNow
            };
        }
        else
        {
            subscription.StripeCustomerId = session.CustomerId;
            subscription.StripeSubscriptionId = stripeSubscriptionId;
            subscription.Status = stripeSub.Status;
            subscription.TrialEndsAt = stripeSub.TrialEnd ?? subscription.TrialEndsAt;
            subscription.CurrentPeriodStart = firstItem.CurrentPeriodStart;
            subscription.CurrentPeriodEnd = firstItem.CurrentPeriodEnd;
            subscription.CanceledAt = null;
        }

        SyncItemsAndPlan(subscription, stripeSub);

        if (isNew)
            await unitOfWork.Subscriptions.AddAsync(subscription);
        else
            unitOfWork.Subscriptions.Update(subscription);

        // Single Source of Truth: Business.PlanTypeId is the canonical plan used by
        // FeatureGateService. Keep it in sync with the Stripe subscription on every mutation.
        var business = await unitOfWork.Business.GetByIdAsync(businessId);
        if (business != null)
        {
            business.PlanTypeId = subscription.PlanTypeId;
            unitOfWork.Business.Update(business);
        }

        await unitOfWork.SaveChangesAsync();
        featureGate.Invalidate(businessId);
        _logger.LogInformation("Checkout completed for business {BusinessId}, subscription {SubId}, plan {PlanId}",
            businessId, stripeSubscriptionId, subscription.PlanTypeId);
    }

    private async Task HandleSubscriptionUpdatedAsync(
        Event stripeEvent, IUnitOfWork unitOfWork, IFeatureGateService featureGate)
    {
        var stripeSub = stripeEvent.Data.Object as Stripe.Subscription;
        if (stripeSub == null) return;

        // Eager-load items so SyncItemsAndPlan can clear-and-replace without
        // leaving orphan rows in SubscriptionItems.
        var subscription = await unitOfWork.Subscriptions
            .GetByStripeSubscriptionIdWithItemsAsync(stripeSub.Id);
        if (subscription == null) return;

        // Temporal guard: discard out-of-order events
        if (IsOutOfOrder(stripeEvent, subscription))
        {
            _logger.LogInformation(
                "Skipping out-of-order subscription.updated event {EventId} for {SubId}",
                stripeEvent.Id, stripeSub.Id);
            return;
        }

        if (stripeSub.Items?.Data == null || stripeSub.Items.Data.Count == 0)
        {
            _logger.LogWarning("subscription.updated: subscription {SubId} has no price items", stripeSub.Id);
            return;
        }

        subscription.Status = stripeSub.Status;
        subscription.CurrentPeriodStart = stripeSub.Items.Data[0].CurrentPeriodStart;
        subscription.CurrentPeriodEnd = stripeSub.Items.Data[0].CurrentPeriodEnd;

        SyncItemsAndPlan(subscription, stripeSub);

        unitOfWork.Subscriptions.Update(subscription);

        // Single Source of Truth: propagate plan change to Business.
        var business = await unitOfWork.Business.GetByIdAsync(subscription.BusinessId);
        if (business != null)
        {
            business.PlanTypeId = subscription.PlanTypeId;
            unitOfWork.Business.Update(business);
        }

        await unitOfWork.SaveChangesAsync();
        featureGate.Invalidate(subscription.BusinessId);

        _logger.LogInformation("Subscription updated: {SubId}, status: {Status}, plan {PlanId}",
            stripeSub.Id, stripeSub.Status, subscription.PlanTypeId);
    }

    private async Task HandleSubscriptionDeletedAsync(
        Event stripeEvent, IUnitOfWork unitOfWork, IFeatureGateService featureGate)
    {
        var stripeSub = stripeEvent.Data.Object as Stripe.Subscription;
        if (stripeSub == null) return;

        var subscription = await unitOfWork.Subscriptions
            .GetByStripeSubscriptionIdAsync(stripeSub.Id);
        if (subscription == null) return;

        if (IsOutOfOrder(stripeEvent, subscription))
        {
            _logger.LogInformation(
                "Skipping out-of-order subscription.deleted event {EventId} for {SubId}",
                stripeEvent.Id, stripeSub.Id);
            return;
        }

        subscription.Status = StripeSubscriptionStatus.Canceled;
        subscription.CanceledAt = DateTime.UtcNow;
        subscription.PlanTypeId = PlanTypeIds.Free;

        unitOfWork.Subscriptions.Update(subscription);

        // Single Source of Truth: downgrade Business to Free when the subscription ends.
        var business = await unitOfWork.Business.GetByIdAsync(subscription.BusinessId);
        if (business != null)
        {
            business.PlanTypeId = PlanTypeIds.Free;
            unitOfWork.Business.Update(business);
        }

        await unitOfWork.SaveChangesAsync();
        featureGate.Invalidate(subscription.BusinessId);

        _logger.LogInformation("Subscription deleted: {SubId}, business {BusinessId} reverted to Free",
            stripeSub.Id, subscription.BusinessId);
    }

    private async Task HandleInvoicePaymentFailedAsync(Event stripeEvent, IUnitOfWork unitOfWork)
    {
        var invoice = stripeEvent.Data.Object as Stripe.Invoice;
        var subscriptionId = invoice?.Parent?.SubscriptionDetails?.SubscriptionId;
        if (invoice == null || string.IsNullOrEmpty(subscriptionId)) return;

        var subscription = await unitOfWork.Subscriptions
            .GetByStripeSubscriptionIdAsync(subscriptionId);
        if (subscription == null) return;

        if (IsOutOfOrder(stripeEvent, subscription))
        {
            _logger.LogInformation(
                "Skipping out-of-order invoice.payment_failed event {EventId} for {SubId}",
                stripeEvent.Id, subscriptionId);
            return;
        }

        subscription.Status = StripeSubscriptionStatus.PastDue;

        unitOfWork.Subscriptions.Update(subscription);
        await unitOfWork.SaveChangesAsync();

        _logger.LogWarning("Payment failed for subscription {SubId}", subscriptionId);
    }

    private async Task HandleInvoicePaymentSucceededAsync(Event stripeEvent, IUnitOfWork unitOfWork)
    {
        var invoice = stripeEvent.Data.Object as Stripe.Invoice;
        var subscriptionId = invoice?.Parent?.SubscriptionDetails?.SubscriptionId;
        if (invoice == null || string.IsNullOrEmpty(subscriptionId)) return;

        var subscription = await unitOfWork.Subscriptions
            .GetByStripeSubscriptionIdAsync(subscriptionId);
        if (subscription == null) return;

        if (IsOutOfOrder(stripeEvent, subscription))
        {
            _logger.LogInformation(
                "Skipping out-of-order invoice.payment_succeeded event {EventId} for {SubId}",
                stripeEvent.Id, subscriptionId);
            return;
        }

        subscription.Status = StripeSubscriptionStatus.Active;

        unitOfWork.Subscriptions.Update(subscription);
        await unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Payment succeeded for subscription {SubId}, status restored to active",
            subscriptionId);
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Returns true if the Stripe event is older than the last subscription update.
    /// Protects against out-of-order webhook delivery.
    /// </summary>
    private static bool IsOutOfOrder(Event stripeEvent, Domain.Models.Subscription subscription)
    {
        return subscription.UpdatedAt > DateTime.MinValue
            && stripeEvent.Created < subscription.UpdatedAt;
    }

    /// <summary>
    /// Clears the local <see cref="Domain.Models.Subscription.Items"/>
    /// collection and rebuilds it from <paramref name="stripeSub"/> so
    /// the local row mirrors Stripe's authoritative state. Each item is
    /// classified as base plan or add-on via <see cref="StripeConstants"/>;
    /// unknown Price IDs raise <see cref="KeyNotFoundException"/> (fail-closed).
    /// After rebuilding, the method validates that exactly one item carries
    /// <see cref="Domain.Models.SubscriptionItem.IsBasePlan"/> and uses it
    /// to resolve <c>PlanTypeId</c>, <c>BillingCycle</c> and <c>PricingGroup</c>.
    /// </summary>
    private static void SyncItemsAndPlan(
        Domain.Models.Subscription subscription,
        Stripe.Subscription stripeSub)
    {
        // Clear-and-replace: EF tracks removals on the loaded collection so
        // the corresponding SubscriptionItems rows get deleted on SaveChanges.
        subscription.Items.Clear();

        var now = DateTime.UtcNow;

        foreach (var item in stripeSub.Items.Data)
        {
            var priceId = item.Price?.Id
                ?? throw new InvalidOperationException(
                    $"Stripe subscription {stripeSub.Id} item {item.Id} has no Price.Id.");

            bool isBase = StripeConstants.IsBasePlan(priceId);
            bool isAddon = StripeConstants.IsAddon(priceId);

            if (!isBase && !isAddon)
            {
                throw new KeyNotFoundException(
                    $"Stripe Price ID '{priceId}' on subscription {stripeSub.Id} is not registered " +
                    "in StripeConstants.PriceMap nor StripeConstants.AddonPriceMap. " +
                    "Add it to the catalog before processing this subscription (fail-closed).");
            }

            subscription.Items.Add(new Domain.Models.SubscriptionItem
            {
                StripeItemId = item.Id,
                StripePriceId = priceId,
                Quantity = (int)item.Quantity,
                IsBasePlan = isBase,
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        // Exactly-one-base-plan invariant — fail-closed so catalog drift or
        // mis-classified add-ons surface as failed events instead of silent
        // tenant downgrades.
        var basePlanItems = subscription.Items.Where(i => i.IsBasePlan).ToList();
        if (basePlanItems.Count == 0)
            throw new InvalidOperationException(
                $"Stripe subscription {stripeSub.Id} has no base plan item.");
        if (basePlanItems.Count > 1)
            throw new InvalidOperationException(
                $"Stripe subscription {stripeSub.Id} has {basePlanItems.Count} base plan items — only one expected.");

        var basePlanPriceId = basePlanItems[0].StripePriceId;
        subscription.PlanTypeId = StripeConstants.ResolvePlanTypeId(basePlanPriceId);
        subscription.BillingCycle = StripeConstants.ResolveBillingCycle(basePlanPriceId);
        subscription.PricingGroup = StripeConstants.ResolvePricingGroup(basePlanPriceId);
    }

    #endregion
}
