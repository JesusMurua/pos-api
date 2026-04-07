using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;

namespace POS.API.Controllers;

/// <summary>
/// Receives webhook events from payment providers (Clip, MercadoPago).
/// Stores the raw payload in the inbox for async processing by the background worker.
/// </summary>
[Route("api/webhooks/payments")]
[ApiController]
[AllowAnonymous]
public class PaymentWebhookController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(IUnitOfWork unitOfWork, ILogger<PaymentWebhookController> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// Receives a webhook event from a payment provider.
    /// Stores the payload in the inbox and returns 200 OK immediately.
    /// Business logic is processed asynchronously by PaymentWebhookProcessorWorker.
    /// </summary>
    /// <param name="provider">The payment provider name (e.g., "mercadopago", "clip").</param>
    [HttpPost("{provider}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> HandleWebhook(string provider)
    {
        using var reader = new StreamReader(Request.Body);
        var rawPayload = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(rawPayload))
            return BadRequest("Empty payload");

        // Extract external event ID from the payload for idempotency
        string externalEventId;
        string eventType;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(rawPayload);
            externalEventId = doc.RootElement.TryGetProperty("id", out var idProp)
                ? idProp.GetString() ?? Guid.NewGuid().ToString()
                : Guid.NewGuid().ToString();
            eventType = doc.RootElement.TryGetProperty("type", out var typeProp)
                ? typeProp.GetString() ?? "unknown"
                : "unknown";
        }
        catch (System.Text.Json.JsonException)
        {
            _logger.LogWarning("Payment webhook from {Provider}: invalid JSON", provider);
            return BadRequest("Invalid JSON payload");
        }

        var inboxEvent = new PaymentWebhookInbox
        {
            Provider = provider.ToLowerInvariant(),
            ExternalEventId = externalEventId,
            EventType = eventType,
            RawPayload = rawPayload,
            Status = WebhookInboxStatus.Pending
        };

        await _unitOfWork.PaymentWebhookInbox.AddAsync(inboxEvent);

        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Duplicate event (unique constraint on Provider + ExternalEventId) — silently ignore
            _logger.LogInformation("Payment webhook duplicate ignored: {Provider}/{EventId}", provider, externalEventId);
            return Ok();
        }

        _logger.LogInformation("Payment webhook queued: {Provider}/{EventType}/{EventId}",
            provider, eventType, externalEventId);

        return Ok();
    }
}
