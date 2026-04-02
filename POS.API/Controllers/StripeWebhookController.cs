using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Domain.Settings;
using POS.Repository;
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

        try
        {
            var inboxEvent = new StripeEventInbox
            {
                StripeEventId = stripeEvent.Id,
                Type = stripeEvent.Type,
                RawJson = json,
                Status = StripeEventStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            await _unitOfWork.StripeEventInbox.AddAsync(inboxEvent);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("Stripe event {EventId} ({Type}) queued for processing",
                stripeEvent.Id, stripeEvent.Type);
        }
        catch (DbUpdateException)
        {
            // Unique constraint violation on StripeEventId — duplicate event, silently ignore
            _logger.LogInformation("Duplicate Stripe event ignored: {EventId}", stripeEvent.Id);
        }

        return Ok();
    }
}
