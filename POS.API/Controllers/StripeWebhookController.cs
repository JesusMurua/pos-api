using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using POS.Domain.Settings;
using POS.Services.IService;
using Stripe;

namespace POS.API.Controllers;

/// <summary>
/// Receives Stripe webhook events and stores them in the inbox for background processing.
/// All heavy lifting is done by StripeEventProcessorWorker.
/// </summary>
[Route("api/stripe/webhook")]
[ApiController]
[AllowAnonymous]
public class StripeWebhookController : ControllerBase
{
    private readonly IStripeService _stripeService;
    private readonly string _webhookSecret;
    private readonly ILogger<StripeWebhookController> _logger;

    public StripeWebhookController(
        IStripeService stripeService,
        IOptions<StripeSettings> stripeSettings,
        ILogger<StripeWebhookController> logger)
    {
        _stripeService = stripeService;
        _webhookSecret = stripeSettings.Value.WebhookSecret;
        _logger = logger;
    }

    /// <summary>
    /// Validates the Stripe signature, inserts the event into the inbox, and returns 200 OK immediately.
    /// Duplicate events are silently ignored via unique constraint on StripeEventId.
    /// </summary>
    [HttpPost]
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

        await _stripeService.QueueWebhookEventAsync(stripeEvent.Id, stripeEvent.Type, json);

        return Ok();
    }
}
