using Microsoft.EntityFrameworkCore;
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
        // Same scoped instance backing the UnitOfWork — used for the PR-2 base-price
        // resolution against the StripePlanPrice catalog + Stripe rail lookup.
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // PR-3: reused to mirror automatic Stripe payments into TenantPayment (§2 model C).
        // Resolved from the SAME scope so it shares this DbContext instance.
        var payments = scope.ServiceProvider.GetRequiredService<IAdminTenantPaymentService>();
        // PR-5: PaymentFailed + TrialConverted notifications.
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var pendingEvents = await unitOfWork.StripeEventInbox.GetPendingEventsAsync(BatchSize);
        if (pendingEvents.Count == 0) return;

        _logger.LogInformation("Processing {Count} pending Stripe events", pendingEvents.Count);

        foreach (var inboxEvent in pendingEvents)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var stripeEvent = EventUtility.ParseEvent(inboxEvent.RawJson);
                await ProcessEventAsync(stripeEvent, unitOfWork, stripeClient, featureGate, db, payments, notifications);

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
        IFeatureGateService featureGate,
        ApplicationDbContext db,
        IAdminTenantPaymentService payments,
        INotificationService notifications)
    {
        switch (stripeEvent.Type)
        {
            case EventTypes.CheckoutSessionCompleted:
                await HandleCheckoutSessionCompletedAsync(stripeEvent, unitOfWork, stripeClient, featureGate, db);
                break;

            case EventTypes.CustomerSubscriptionUpdated:
                await HandleSubscriptionUpdatedAsync(stripeEvent, unitOfWork, featureGate, db);
                break;

            case EventTypes.CustomerSubscriptionDeleted:
                await HandleSubscriptionDeletedAsync(stripeEvent, unitOfWork, featureGate);
                break;

            case EventTypes.InvoicePaymentFailed:
                await HandleInvoicePaymentFailedAsync(stripeEvent, unitOfWork, notifications);
                break;

            case EventTypes.InvoicePaymentSucceeded:
                await HandleInvoicePaymentSucceededAsync(stripeEvent, unitOfWork, db, payments, notifications);
                break;

            default:
                _logger.LogInformation("Unhandled Stripe event type: {Type}", stripeEvent.Type);
                break;
        }
    }

    private async Task HandleCheckoutSessionCompletedAsync(
        Event stripeEvent, IUnitOfWork unitOfWork, IStripeClient stripeClient, IFeatureGateService featureGate,
        ApplicationDbContext db)
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

        // Eager-load active add-ons so SyncItemsAndPlanAsync can upsert/deactivate them in memory.
        var subscription = await unitOfWork.Subscriptions
            .GetByStripeSubscriptionIdWithAddOnsAsync(stripeSubscriptionId);
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

        await SyncItemsAndPlanAsync(subscription, stripeSub, db);

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
        Event stripeEvent, IUnitOfWork unitOfWork, IFeatureGateService featureGate, ApplicationDbContext db)
    {
        var stripeSub = stripeEvent.Data.Object as Stripe.Subscription;
        if (stripeSub == null) return;

        // Eager-load active add-ons so SyncItemsAndPlanAsync can upsert/deactivate them in memory.
        var subscription = await unitOfWork.Subscriptions
            .GetByStripeSubscriptionIdWithAddOnsAsync(stripeSub.Id);
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

        await SyncItemsAndPlanAsync(subscription, stripeSub, db);

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

    private async Task HandleInvoicePaymentFailedAsync(
        Event stripeEvent, IUnitOfWork unitOfWork, INotificationService notifications)
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

        await notifications.EnqueueAsync("PaymentFailed", NotificationRecipientType.BillingEmail,
            subscription.BusinessId, new Dictionary<string, string>());

        unitOfWork.Subscriptions.Update(subscription);
        await unitOfWork.SaveChangesAsync();

        _logger.LogWarning("Payment failed for subscription {SubId}", subscriptionId);
    }

    private async Task HandleInvoicePaymentSucceededAsync(
        Event stripeEvent, IUnitOfWork unitOfWork, ApplicationDbContext db, IAdminTenantPaymentService payments,
        INotificationService notifications)
    {
        var stripeInvoice = stripeEvent.Data.Object as Stripe.Invoice;
        var subscriptionId = stripeInvoice?.Parent?.SubscriptionDetails?.SubscriptionId;
        if (stripeInvoice == null || string.IsNullOrEmpty(subscriptionId)) return;

        var subscription = await unitOfWork.Subscriptions
            .GetByStripeSubscriptionIdAsync(subscriptionId);
        if (subscription == null) return;

        // Subscription-status restore stays guarded by the out-of-order check (it mutates
        // Subscription fields). The invoice mirror below is NOT gated by it — the mirror is
        // idempotent on StripeInvoiceId, so a replayed/out-of-order event simply no-ops.
        if (!IsOutOfOrder(stripeEvent, subscription))
        {
            // TrialConverted fires once on the trialing → active transition (first paid invoice).
            var wasTrialing = subscription.Status == StripeSubscriptionStatus.Trialing;

            subscription.Status = StripeSubscriptionStatus.Active;
            unitOfWork.Subscriptions.Update(subscription);

            if (wasTrialing)
                await notifications.EnqueueAsync("TrialConverted", NotificationRecipientType.Owner,
                    subscription.BusinessId,
                    new Dictionary<string, string> { ["plan"] = PlanTypeIds.ToCode(subscription.PlanTypeId) });

            await unitOfWork.SaveChangesAsync();
        }

        // Stripe SSoT mirror (§2 model C): replicate the Stripe invoice locally and record
        // the automatic payment. NEVER apply local IVA here — Stripe already computed the
        // tax; we copy its amounts (Subtotal/Total). The backend IVA path is the manual-rail
        // generation job (InvoiceGenerationService), which Stripe-rail invoices never touch.
        var stripeRailId = await db.SaaSBillingMethods
            .Where(m => m.Code == "Stripe").Select(m => (int?)m.Id).FirstOrDefaultAsync();
        if (stripeRailId == null)
        {
            _logger.LogWarning("No Stripe billing rail seeded — cannot mirror invoice {StripeInvoiceId}",
                stripeInvoice.Id);
            return;
        }

        var invoice = await db.SubscriptionInvoices
            .FirstOrDefaultAsync(i => i.StripeInvoiceId == stripeInvoice.Id);
        if (invoice == null)
        {
            var invoiceNumber = await unitOfWork.Business.IncrementInvoiceCounterAsync(subscription.BusinessId);
            invoice = new Domain.Models.SubscriptionInvoice
            {
                SubscriptionId = subscription.Id,
                BusinessId = subscription.BusinessId,
                InvoiceNumber = invoiceNumber,
                Status = SubscriptionInvoiceStatus.Open, // RecordAsync recomputes after the payment
                IssuedAtUtc = DateTime.UtcNow,
                DueDate = stripeInvoice.DueDate ?? DateTime.UtcNow,
                PeriodStart = stripeInvoice.PeriodStart,
                PeriodEnd = stripeInvoice.PeriodEnd,
                SubtotalCents = (int)stripeInvoice.Subtotal,
                TaxCents = (int)(stripeInvoice.Total - stripeInvoice.Subtotal),
                TotalCents = (int)stripeInvoice.Total,
                Currency = stripeInvoice.Currency?.ToUpperInvariant() ?? subscription.Currency,
                StripeInvoiceId = stripeInvoice.Id
            };
            db.SubscriptionInvoices.Add(invoice);
            await db.SaveChangesAsync();
        }

        await payments.RecordAsync(
            invoiceId: invoice.Id,
            billingMethodId: stripeRailId.Value,
            amountCents: (int)stripeInvoice.AmountPaid,
            currency: invoice.Currency,
            paidAtUtc: DateTime.UtcNow,
            reference: stripeInvoice.Id,   // unique per Stripe invoice → idempotency key
            notes: null,
            receivedByTokenIdHash: null,   // automatic (webhook)
            stripeChargeId: stripeInvoice.Id,
            rawWebhookPayloadJson: stripeEvent.ToJson());

        _logger.LogInformation("Payment succeeded for subscription {SubId}; invoice {InvoiceNumber} mirrored",
            subscriptionId, invoice.InvoiceNumber);
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
    /// Reconciles <paramref name="subscription"/> with <paramref name="stripeSub"/>. The base
    /// plan resolves (PR-2) as <b>catalog</b> (<c>StripePlanPrice</c>) → <b>custom</b>
    /// (<c>price.Metadata["planTypeId"]</c>, <c>kind=custom</c>) → <b>fail-closed</b>.
    /// Add-ons (PR-4) resolve as <b>catalog</b> (<c>PlanAddOn.StripePriceId</c>) → <b>custom-addon</b>
    /// (<c>kind=custom-addon</c> + <c>metadata.addOnId</c>) → <b>fail-closed</b>, and are synced
    /// into <see cref="Domain.Models.Subscription.AddOns"/>. Populates the frozen Stripe columns
    /// and the <c>BillingMethodId</c> (Stripe rail).
    /// </summary>
    private static async Task SyncItemsAndPlanAsync(
        Domain.Models.Subscription subscription,
        Stripe.Subscription stripeSub,
        ApplicationDbContext db)
    {
        var now = DateTime.UtcNow;
        var planCatalog = (await db.StripePlanPrices.AsNoTracking().ToListAsync())
            .ToDictionary(p => p.StripePriceId, StringComparer.Ordinal);
        var addOnCatalog = (await db.PlanAddOns.AsNoTracking().Where(a => a.StripePriceId != null).ToListAsync())
            .ToDictionary(a => a.StripePriceId!, a => a, StringComparer.Ordinal);

        // ── Classify every Stripe item into exactly one base + N add-ons. ──
        Stripe.SubscriptionItem? baseStripeItem = null;
        // (Stripe item, resolved PlanAddOn id) for the add-on sync below.
        var addOnHits = new List<(Stripe.SubscriptionItem Item, int PlanAddOnId)>();

        foreach (var item in stripeSub.Items.Data)
        {
            var priceId = item.Price?.Id
                ?? throw new InvalidOperationException(
                    $"Stripe subscription {stripeSub.Id} item {item.Id} has no Price.Id.");
            var kind = item.Price?.Metadata?.GetValueOrDefault("kind");

            bool isBase = planCatalog.ContainsKey(priceId)
                || string.Equals(kind, "custom", StringComparison.OrdinalIgnoreCase);

            if (isBase)
            {
                if (baseStripeItem != null)
                    throw new InvalidOperationException(
                        $"Stripe subscription {stripeSub.Id} has more than one base plan item — only one expected.");
                baseStripeItem = item;
                continue;
            }

            // Add-on: catalog by price id, or custom-addon by metadata.addOnId. Fail-closed otherwise.
            if (addOnCatalog.TryGetValue(priceId, out var planAddOn))
            {
                addOnHits.Add((item, planAddOn.Id));
            }
            else if (string.Equals(kind, "custom-addon", StringComparison.OrdinalIgnoreCase)
                     && int.TryParse(item.Price?.Metadata?.GetValueOrDefault("addOnId"), out var customAddOnId))
            {
                addOnHits.Add((item, customAddOnId));
            }
            else
            {
                throw new KeyNotFoundException(
                    $"Stripe Price ID '{priceId}' on subscription {stripeSub.Id} is not a base plan " +
                    "(StripePlanPrice / kind=custom), nor a catalog/custom add-on (PlanAddOn / kind=custom-addon) — fail-closed.");
            }
        }

        if (baseStripeItem == null)
            throw new InvalidOperationException($"Stripe subscription {stripeSub.Id} has no base plan item.");

        // ── Resolve the base plan (catalog → custom metadata). ──
        var basePriceId = baseStripeItem.Price!.Id;
        if (planCatalog.TryGetValue(basePriceId, out var cat))
        {
            subscription.PlanTypeId = cat.PlanTypeId;
            subscription.BillingCycle = cat.BillingCycle;
            subscription.PricingGroup = cat.PricingGroup;
        }
        else
        {
            var meta = baseStripeItem.Price?.Metadata;
            if (meta == null || !int.TryParse(meta.GetValueOrDefault("planTypeId"), out var planTypeId))
                throw new KeyNotFoundException(
                    $"Custom Stripe Price '{basePriceId}' on subscription {stripeSub.Id} has no resolvable " +
                    "planTypeId metadata (fail-closed).");
            subscription.PlanTypeId = planTypeId;
            subscription.BillingCycle = meta.GetValueOrDefault("billingCycle")
                ?? (string.Equals(baseStripeItem.Price?.Recurring?.Interval, "year", StringComparison.OrdinalIgnoreCase) ? "Annual" : "Monthly");
            subscription.PricingGroup = meta.GetValueOrDefault("pricingGroup") ?? "General";
        }

        // Frozen Stripe truth + the rail. BaseAmountCents = the Stripe unit_amount.
        subscription.StripePriceId = basePriceId;
        subscription.StripeBaseItemId = baseStripeItem.Id;
        subscription.BaseAmountCents = (int?)baseStripeItem.Price?.UnitAmount;
        subscription.BillingMethodId = await db.SaaSBillingMethods
            .Where(m => m.Code == "Stripe").Select(m => (int?)m.Id).FirstOrDefaultAsync();

        // ── Add-on sync. IMPORTANT: SubscriptionAddOn is NOT a pure mirror of
        //    stripe_subscription.items — it carries local-only state that does not exist in
        //    Stripe: CustomPriceCents, Reason, ActivatedByTokenIdHash, LastProRatedInvoiceId.
        //    DO NOT use .Clear() + rebuild (the legacy SubscriptionItem pattern, now retired) —
        //    that would silently destroy admin-entered data. Instead UPSERT by StripeItemId
        //    (update Quantity if present, insert if new) and SOFT-DEACTIVATE (set DeactivatedAt,
        //    never DELETE) the ones that vanished from Stripe, preserving history (§6). ──
        var seenStripeItemIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (item, planAddOnId) in addOnHits)
        {
            seenStripeItemIds.Add(item.Id);
            var existing = subscription.AddOns
                .FirstOrDefault(a => a.DeactivatedAt == null && a.StripeItemId == item.Id);
            if (existing != null)
            {
                existing.Quantity = (int)item.Quantity;
            }
            else
            {
                subscription.AddOns.Add(new Domain.Models.SubscriptionAddOn
                {
                    AddOnId = planAddOnId,
                    Quantity = (int)item.Quantity,
                    ActivatedAt = now,
                    StripeItemId = item.Id,
                    StripeAddOnPriceId = item.Price?.Id
                });
            }
        }

        foreach (var local in subscription.AddOns.Where(a => a.DeactivatedAt == null && a.StripeItemId != null))
        {
            if (!seenStripeItemIds.Contains(local.StripeItemId!))
                local.DeactivatedAt = now;
        }
    }

    #endregion
}
