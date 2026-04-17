using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using POS.Domain.Enums;
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
    private readonly ILogger<StripeService> _logger;
    private readonly IConfiguration _configuration;

    public StripeService(
        IUnitOfWork unitOfWork,
        IStripeClient stripeClient,
        ILogger<StripeService> logger,
        IConfiguration configuration)
    {
        _unitOfWork = unitOfWork;
        _stripeClient = stripeClient;
        _logger = logger;
        _configuration = configuration;
    }

    #region Public API Methods

    /// <inheritdoc />
    public async Task<string> CreateCheckoutSessionAsync(
        int businessId, string priceId, string successUrl, string cancelUrl)
    {
        // Whitelist: reject any priceId not present in our curated PriceMap.
        // ResolvePlanType silently returns "Free" for unknowns, so we hit the dictionary directly.
        if (!StripeConstants.PriceMap.ContainsKey(priceId))
            throw new BadHttpRequestException("Price ID inválido");

        ValidateRedirectUrl(successUrl, nameof(successUrl));
        ValidateRedirectUrl(cancelUrl, nameof(cancelUrl));

        var business = await _unitOfWork.Business.GetByIdAsync(businessId)
            ?? throw new NotFoundException($"Business with id {businessId} not found");

        // Idempotency: prevent a second concurrent paid subscription for the same business.
        // Mutations to an existing live sub belong in the Stripe Customer Portal.
        var existing = await _unitOfWork.Subscriptions.GetByBusinessIdAsync(businessId);
        if (existing != null
            && (existing.Status == StripeSubscriptionStatus.Active
                || existing.Status == StripeSubscriptionStatus.Trialing))
        {
            throw new BadHttpRequestException(
                "El negocio ya tiene una suscripción activa. Utilice el portal de cliente para modificarla.");
        }

        var stripeCustomerId = await GetOrCreateStripeCustomerAsync(business);

        // Honor the in-app trial: if the business still has trial days left, hand them
        // over to Stripe so the customer is billed only after the grace window ends.
        // Stripe requires TrialPeriodDays >= 1 (we use >= 2 for a 48h safety margin).
        var subscriptionData = new SessionSubscriptionDataOptions
        {
            Metadata = new Dictionary<string, string>
            {
                { "businessId", businessId.ToString() }
            }
        };

        var remainingTrialDays = business.TrialEndsAt.HasValue
            ? (int)Math.Floor((business.TrialEndsAt.Value - DateTime.UtcNow).TotalDays)
            : 0;

        if (remainingTrialDays >= 2)
        {
            subscriptionData.TrialPeriodDays = remainingTrialDays;
        }

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
            SubscriptionData = subscriptionData,
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

    /// <inheritdoc />
    public async Task QueueWebhookEventAsync(string stripeEventId, string eventType, string rawJson)
    {
        try
        {
            var inboxEvent = new StripeEventInbox
            {
                StripeEventId = stripeEventId,
                Type = eventType,
                RawJson = rawJson,
                Status = StripeEventStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.StripeEventInbox.AddAsync(inboxEvent);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Stripe event {EventId} ({Type}) queued for processing",
                stripeEventId, eventType);
        }
        catch (DbUpdateException)
        {
            // Unique constraint violation on StripeEventId — duplicate event, silently ignore
            _logger.LogInformation("Duplicate Stripe event ignored: {EventId}", stripeEventId);
        }
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Open-redirect guard: accept only relative URLs or absolute URLs whose origin
    /// matches one of the configured Cors:Origins entries.
    /// </summary>
    private void ValidateRedirectUrl(string url, string paramName)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new BadHttpRequestException($"{paramName} es requerido");

        if (Uri.TryCreate(url, UriKind.Relative, out _)) return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed))
            throw new BadHttpRequestException($"{paramName} inválido");

        var allowedOrigins = _configuration.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
        var requestOrigin = $"{parsed.Scheme}://{parsed.Authority}";

        if (!allowedOrigins.Any(o =>
                string.Equals(o.TrimEnd('/'), requestOrigin, StringComparison.OrdinalIgnoreCase)))
        {
            throw new BadHttpRequestException($"{paramName} apunta a un origen no permitido");
        }
    }

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
