using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using POS.Domain.Settings;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Receives Facturapi webhook events for invoice status updates.
/// Validates the webhook secret and delegates processing to InvoicingService.
/// </summary>
[Route("api/webhooks/facturapi")]
[ApiController]
[AllowAnonymous]
public class FacturapiWebhookController : ControllerBase
{
    private readonly IInvoicingService _invoicingService;
    private readonly string _webhookSecret;
    private readonly ILogger<FacturapiWebhookController> _logger;

    public FacturapiWebhookController(
        IInvoicingService invoicingService,
        IOptions<FacturapiSettings> settings,
        ILogger<FacturapiWebhookController> logger)
    {
        _invoicingService = invoicingService;
        _webhookSecret = settings.Value.WebhookSecret;
        _logger = logger;
    }

    /// <summary>
    /// Receives a Facturapi webhook event.
    /// Validates the X-Facturapi-Webhook-Secret header, then processes the event.
    /// </summary>
    /// <response code="200">Event processed successfully.</response>
    /// <response code="401">Invalid or missing webhook secret.</response>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> HandleWebhook()
    {
        // Validate webhook secret
        var providedSecret = Request.Headers["X-Facturapi-Webhook-Secret"].FirstOrDefault();
        if (!string.IsNullOrEmpty(_webhookSecret) && providedSecret != _webhookSecret)
        {
            _logger.LogWarning("Facturapi webhook secret validation failed");
            return Unauthorized("Invalid webhook secret");
        }

        using var reader = new StreamReader(Request.Body);
        var json = await reader.ReadToEndAsync();

        // Extract event type from JSON
        string? eventType = null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("type", out var typeProp))
                eventType = typeProp.GetString();
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning("Facturapi webhook: invalid JSON — {Message}", ex.Message);
            return BadRequest("Invalid JSON payload");
        }

        if (string.IsNullOrEmpty(eventType))
        {
            _logger.LogWarning("Facturapi webhook: missing event type");
            return BadRequest("Missing event type");
        }

        _logger.LogInformation("Facturapi webhook received: {EventType}", eventType);

        try
        {
            await _invoicingService.ProcessWebhookAsync(eventType, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Facturapi webhook event {EventType}", eventType);
            // Return 200 to prevent Facturapi from retrying — we logged the error
        }

        return Ok();
    }
}
