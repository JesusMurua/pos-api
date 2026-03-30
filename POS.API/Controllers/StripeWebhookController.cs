using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using POS.Domain.Models;
using POS.Domain.Settings;
using POS.Repository;
using Stripe;

namespace POS.API.Controllers;

/// <summary>
/// Handles incoming Stripe webhook events for subscription lifecycle management.
/// </summary>
[Route("api/stripe/webhook")]
[ApiController]
[AllowAnonymous]
public class StripeWebhookController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly string _webhookSecret;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        IUnitOfWork unitOfWork,
        IOptions<StripeSettings> stripeSettings,
        ILogger<StripeWebhookController> logger)
    {
        _unitOfWork = unitOfWork;
        _webhookSecret = stripeSettings.Value.WebhookSecret;
        _logger = logger;
    }

    /// <summary>
    /// Receives and processes Stripe webhook events.
    /// </summary>
    [HttpPost]
    [DisableRequestSizeLimit]
    public async Task<IActionResult> HandleWebhook()
    {
        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync();

        Event stripeEvent;
        try
        {
            stripeEvent = EventUtility.ConstructEvent(
                json,
                Request.Headers["Stripe-Signature"],
                _webhookSecret);
        }
        catch (StripeException ex)
        {
            _logger.LogWarning("Stripe webhook signature verification failed: {Message}", ex.Message);
            return BadRequest("Invalid signature");
        }

        try
        {
            switch (stripeEvent.Type)
            {
                case EventTypes.CheckoutSessionCompleted:
                    await HandleCheckoutSessionCompletedAsync(stripeEvent);
                    break;

                case EventTypes.CustomerSubscriptionUpdated:
                    await HandleSubscriptionUpdatedAsync(stripeEvent);
                    break;

                case EventTypes.CustomerSubscriptionDeleted:
                    await HandleSubscriptionDeletedAsync(stripeEvent);
                    break;

                case EventTypes.InvoicePaymentFailed:
                    await HandleInvoicePaymentFailedAsync(stripeEvent);
                    break;

                default:
                    _logger.LogInformation("Unhandled Stripe event type: {Type}", stripeEvent.Type);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Stripe webhook event {Type}", stripeEvent.Type);
        }

        return Ok();
    }

    #region Private Event Handlers

    private async Task HandleCheckoutSessionCompletedAsync(Event stripeEvent)
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

        // Fetch full subscription from Stripe to get price and period details
        var subscriptionService = new SubscriptionService();
        var stripeSub = await subscriptionService.GetAsync(stripeSubscriptionId);
        var priceId = stripeSub.Items.Data[0].Price.Id;

        var subscription = await _unitOfWork.Subscriptions.GetByBusinessIdAsync(businessId);
        if (subscription == null)
        {
            subscription = new Domain.Models.Subscription
            {
                BusinessId = businessId,
                StripeCustomerId = session.CustomerId,
                StripeSubscriptionId = stripeSubscriptionId,
                StripePriceId = priceId,
                PlanType = ResolvePlanType(priceId),
                BillingCycle = ResolveBillingCycle(priceId),
                PricingGroup = ResolvePricingGroup(priceId),
                Status = stripeSub.Status,
                TrialEndsAt = stripeSub.TrialEnd ?? DateTime.UtcNow,
                CurrentPeriodStart = stripeSub.Items.Data[0].CurrentPeriodStart,
                CurrentPeriodEnd = stripeSub.Items.Data[0].CurrentPeriodEnd,
                UpdatedAt = DateTime.UtcNow
            };
            await _unitOfWork.Subscriptions.AddAsync(subscription);
        }
        else
        {
            subscription.StripeCustomerId = session.CustomerId;
            subscription.StripeSubscriptionId = stripeSubscriptionId;
            subscription.StripePriceId = priceId;
            subscription.PlanType = ResolvePlanType(priceId);
            subscription.BillingCycle = ResolveBillingCycle(priceId);
            subscription.PricingGroup = ResolvePricingGroup(priceId);
            subscription.Status = stripeSub.Status;
            subscription.TrialEndsAt = stripeSub.TrialEnd ?? subscription.TrialEndsAt;
            subscription.CurrentPeriodStart = stripeSub.Items.Data[0].CurrentPeriodStart;
            subscription.CurrentPeriodEnd = stripeSub.Items.Data[0].CurrentPeriodEnd;
            subscription.CanceledAt = null;
            _unitOfWork.Subscriptions.Update(subscription);
        }

        await _unitOfWork.SaveChangesAsync();
        _logger.LogInformation("Checkout completed for business {BusinessId}, subscription {SubId}",
            businessId, stripeSubscriptionId);
    }

    private async Task HandleSubscriptionUpdatedAsync(Event stripeEvent)
    {
        var stripeSub = stripeEvent.Data.Object as Stripe.Subscription;
        if (stripeSub == null) return;

        var subscription = await _unitOfWork.Subscriptions
            .GetByStripeSubscriptionIdAsync(stripeSub.Id);
        if (subscription == null) return;

        var priceId = stripeSub.Items.Data[0].Price.Id;

        subscription.Status = stripeSub.Status;
        subscription.StripePriceId = priceId;
        subscription.PlanType = ResolvePlanType(priceId);
        subscription.BillingCycle = ResolveBillingCycle(priceId);
        subscription.PricingGroup = ResolvePricingGroup(priceId);
        subscription.CurrentPeriodStart = stripeSub.Items.Data[0].CurrentPeriodStart;
        subscription.CurrentPeriodEnd = stripeSub.Items.Data[0].CurrentPeriodEnd;

        _unitOfWork.Subscriptions.Update(subscription);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Subscription updated: {SubId}, status: {Status}",
            stripeSub.Id, stripeSub.Status);
    }

    private async Task HandleSubscriptionDeletedAsync(Event stripeEvent)
    {
        var stripeSub = stripeEvent.Data.Object as Stripe.Subscription;
        if (stripeSub == null) return;

        var subscription = await _unitOfWork.Subscriptions
            .GetByStripeSubscriptionIdAsync(stripeSub.Id);
        if (subscription == null) return;

        subscription.Status = "canceled";
        subscription.CanceledAt = DateTime.UtcNow;

        _unitOfWork.Subscriptions.Update(subscription);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Subscription deleted: {SubId}", stripeSub.Id);
    }

    private async Task HandleInvoicePaymentFailedAsync(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        var subscriptionId = invoice?.Parent?.SubscriptionDetails?.SubscriptionId;
        if (invoice == null || string.IsNullOrEmpty(subscriptionId)) return;

        var subscription = await _unitOfWork.Subscriptions
            .GetByStripeSubscriptionIdAsync(subscriptionId);
        if (subscription == null) return;

        subscription.Status = "past_due";

        _unitOfWork.Subscriptions.Update(subscription);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogWarning("Payment failed for subscription {SubId}", subscriptionId);
    }

    #endregion

    #region Private Helper Methods

    private static readonly Dictionary<string, (string Plan, string Cycle, string Group)> PriceMap = new()
    {
        // Basico
        { Domain.Helpers.StripeConstants.Basico.General.Monthly, ("Basico", "Monthly", "General") },
        { Domain.Helpers.StripeConstants.Basico.General.Annual, ("Basico", "Annual", "General") },
        { Domain.Helpers.StripeConstants.Basico.Standard.Monthly, ("Basico", "Monthly", "Standard") },
        { Domain.Helpers.StripeConstants.Basico.Standard.Annual, ("Basico", "Annual", "Standard") },
        { Domain.Helpers.StripeConstants.Basico.Restaurant.Monthly, ("Basico", "Monthly", "Restaurant") },
        { Domain.Helpers.StripeConstants.Basico.Restaurant.Annual, ("Basico", "Annual", "Restaurant") },
        // Pro
        { Domain.Helpers.StripeConstants.Pro.General.Monthly, ("Pro", "Monthly", "General") },
        { Domain.Helpers.StripeConstants.Pro.General.Annual, ("Pro", "Annual", "General") },
        { Domain.Helpers.StripeConstants.Pro.Standard.Monthly, ("Pro", "Monthly", "Standard") },
        { Domain.Helpers.StripeConstants.Pro.Standard.Annual, ("Pro", "Annual", "Standard") },
        { Domain.Helpers.StripeConstants.Pro.Restaurant.Monthly, ("Pro", "Monthly", "Restaurant") },
        { Domain.Helpers.StripeConstants.Pro.Restaurant.Annual, ("Pro", "Annual", "Restaurant") },
        // Enterprise
        { Domain.Helpers.StripeConstants.Enterprise.General.Monthly, ("Enterprise", "Monthly", "General") },
        { Domain.Helpers.StripeConstants.Enterprise.General.Annual, ("Enterprise", "Annual", "General") },
        { Domain.Helpers.StripeConstants.Enterprise.Standard.Monthly, ("Enterprise", "Monthly", "Standard") },
        { Domain.Helpers.StripeConstants.Enterprise.Standard.Annual, ("Enterprise", "Annual", "Standard") },
        { Domain.Helpers.StripeConstants.Enterprise.Restaurant.Monthly, ("Enterprise", "Monthly", "Restaurant") },
        { Domain.Helpers.StripeConstants.Enterprise.Restaurant.Annual, ("Enterprise", "Annual", "Restaurant") },
    };

    private static string ResolvePlanType(string priceId) =>
        PriceMap.TryGetValue(priceId, out var info) ? info.Plan : "Free";

    private static string ResolveBillingCycle(string priceId) =>
        PriceMap.TryGetValue(priceId, out var info) ? info.Cycle : "Monthly";

    private static string ResolvePricingGroup(string priceId) =>
        PriceMap.TryGetValue(priceId, out var info) ? info.Group : "General";

    #endregion
}
