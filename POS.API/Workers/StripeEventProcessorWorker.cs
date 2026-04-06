using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
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

        var pendingEvents = await unitOfWork.StripeEventInbox.GetPendingEventsAsync(BatchSize);
        if (pendingEvents.Count == 0) return;

        _logger.LogInformation("Processing {Count} pending Stripe events", pendingEvents.Count);

        foreach (var inboxEvent in pendingEvents)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var stripeEvent = EventUtility.ParseEvent(inboxEvent.RawJson);
                await ProcessEventAsync(stripeEvent, unitOfWork, stripeClient);

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

    private async Task ProcessEventAsync(Event stripeEvent, IUnitOfWork unitOfWork, IStripeClient stripeClient)
    {
        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                await HandleCheckoutSessionCompletedAsync(stripeEvent, unitOfWork, stripeClient);
                break;

            case EventTypes.CustomerSubscriptionUpdated:
                await HandleSubscriptionUpdatedAsync(stripeEvent, unitOfWork);
                break;

            case EventTypes.CustomerSubscriptionDeleted:
                await HandleSubscriptionDeletedAsync(stripeEvent, unitOfWork);
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
        Event stripeEvent, IUnitOfWork unitOfWork, IStripeClient stripeClient)
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

        var priceId = stripeSub.Items?.Data?.FirstOrDefault()?.Price?.Id;
        if (string.IsNullOrEmpty(priceId))
        {
            _logger.LogWarning("checkout.session.completed: subscription {SubId} has no price items",
                stripeSubscriptionId);
            return;
        }

        var firstItem = stripeSub.Items!.Data![0];

        var subscription = await unitOfWork.Subscriptions.GetByBusinessIdAsync(businessId);
        if (subscription == null)
        {
            subscription = new Domain.Models.Subscription
            {
                BusinessId = businessId,
                StripeCustomerId = session.CustomerId,
                StripeSubscriptionId = stripeSubscriptionId,
                StripePriceId = priceId,
                PlanType = StripeConstants.ResolvePlanType(priceId),
                BillingCycle = StripeConstants.ResolveBillingCycle(priceId),
                PricingGroup = StripeConstants.ResolvePricingGroup(priceId),
                Status = stripeSub.Status,
                TrialEndsAt = stripeSub.TrialEnd ?? DateTime.UtcNow,
                CurrentPeriodStart = firstItem.CurrentPeriodStart,
                CurrentPeriodEnd = firstItem.CurrentPeriodEnd,
                UpdatedAt = DateTime.UtcNow
            };
            await unitOfWork.Subscriptions.AddAsync(subscription);
        }
        else
        {
            subscription.StripeCustomerId = session.CustomerId;
            subscription.StripeSubscriptionId = stripeSubscriptionId;
            subscription.StripePriceId = priceId;
            subscription.PlanType = StripeConstants.ResolvePlanType(priceId);
            subscription.BillingCycle = StripeConstants.ResolveBillingCycle(priceId);
            subscription.PricingGroup = StripeConstants.ResolvePricingGroup(priceId);
            subscription.Status = stripeSub.Status;
            subscription.TrialEndsAt = stripeSub.TrialEnd ?? subscription.TrialEndsAt;
            subscription.CurrentPeriodStart = firstItem.CurrentPeriodStart;
            subscription.CurrentPeriodEnd = firstItem.CurrentPeriodEnd;
            subscription.CanceledAt = null;
            unitOfWork.Subscriptions.Update(subscription);
        }

        await unitOfWork.SaveChangesAsync();
        _logger.LogInformation("Checkout completed for business {BusinessId}, subscription {SubId}",
            businessId, stripeSubscriptionId);
    }

    private async Task HandleSubscriptionUpdatedAsync(Event stripeEvent, IUnitOfWork unitOfWork)
    {
        var stripeSub = stripeEvent.Data.Object as Stripe.Subscription;
        if (stripeSub == null) return;

        var subscription = await unitOfWork.Subscriptions
            .GetByStripeSubscriptionIdAsync(stripeSub.Id);
        if (subscription == null) return;

        // Temporal guard: discard out-of-order events
        if (IsOutOfOrder(stripeEvent, subscription))
        {
            _logger.LogInformation(
                "Skipping out-of-order subscription.updated event {EventId} for {SubId}",
                stripeEvent.Id, stripeSub.Id);
            return;
        }

        var priceId = stripeSub.Items?.Data?.FirstOrDefault()?.Price?.Id;
        if (string.IsNullOrEmpty(priceId))
        {
            _logger.LogWarning("subscription.updated: subscription {SubId} has no price items", stripeSub.Id);
            return;
        }

        subscription.Status = stripeSub.Status;
        subscription.StripePriceId = priceId;
        subscription.PlanType = StripeConstants.ResolvePlanType(priceId);
        subscription.BillingCycle = StripeConstants.ResolveBillingCycle(priceId);
        subscription.PricingGroup = StripeConstants.ResolvePricingGroup(priceId);
        subscription.CurrentPeriodStart = stripeSub.Items!.Data![0].CurrentPeriodStart;
        subscription.CurrentPeriodEnd = stripeSub.Items!.Data![0].CurrentPeriodEnd;

        unitOfWork.Subscriptions.Update(subscription);
        await unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Subscription updated: {SubId}, status: {Status}",
            stripeSub.Id, stripeSub.Status);
    }

    private async Task HandleSubscriptionDeletedAsync(Event stripeEvent, IUnitOfWork unitOfWork)
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

        unitOfWork.Subscriptions.Update(subscription);
        await unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Subscription deleted: {SubId}", stripeSub.Id);
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

    #endregion
}
