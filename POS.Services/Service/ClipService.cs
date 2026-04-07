using System.Text.Json;
using Microsoft.Extensions.Logging;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class ClipService : IClipService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOrderService _orderService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<ClipService> _logger;

    public ClipService(
        IUnitOfWork unitOfWork,
        IOrderService orderService,
        HttpClient httpClient,
        ILogger<ClipService> logger)
    {
        _unitOfWork = unitOfWork;
        _orderService = orderService;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Creates a payment intent on Clip and registers a pending payment on the order.
    /// Currently uses a placeholder — real Clip API integration will replace the HTTP call.
    /// </summary>
    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(int branchId, string orderId, int amountCents)
    {
        var config = await _unitOfWork.BranchPaymentConfigs
            .GetByBranchAndProviderAsync(branchId, "clip")
            ?? throw new ValidationException(
                "Clip is not configured for this branch. Add a payment config first.");

        if (!config.IsActive)
            throw new ValidationException("Clip integration is disabled for this branch.");

        // ── Placeholder: Clip API call ──
        // In production, this would POST to Clip's API to push a payment request
        // to the physical terminal identified by config.TerminalId,
        // using config.AccessToken as the Bearer token.
        var externalTransactionId = $"clip_intent_{Guid.NewGuid():N}";

        _logger.LogInformation(
            "Clip payment intent created for order {OrderId}: {ExternalId} ({AmountCents} cents, terminal: {TerminalId})",
            orderId, externalTransactionId, amountCents, config.TerminalId);

        var payment = new OrderPayment
        {
            Method = PaymentMethod.Clip,
            AmountCents = amountCents,
            PaymentProvider = "clip",
            ExternalTransactionId = externalTransactionId,
            PaymentMetadata = config.TerminalId != null
                ? JsonSerializer.Serialize(new { terminalId = config.TerminalId })
                : null,
            Status = PaymentStatus.Pending
        };

        await _orderService.AddPaymentAsync(orderId, branchId, payment);

        return new PaymentIntentResult
        {
            ExternalTransactionId = externalTransactionId,
            Status = PaymentStatus.Pending
        };
    }

    /// <summary>
    /// Processes a webhook event from the PaymentWebhookInbox.
    /// Parses the payload, extracts the reference, and confirms the payment.
    /// </summary>
    public async Task ProcessWebhookAsync(PaymentWebhookInbox inboxEvent)
    {
        using var doc = JsonDocument.Parse(inboxEvent.RawPayload);
        var root = doc.RootElement;

        var externalTransactionId = ExtractExternalReference(root);
        if (string.IsNullOrEmpty(externalTransactionId))
        {
            _logger.LogWarning(
                "Clip webhook {EventId}: could not extract external reference from payload",
                inboxEvent.ExternalEventId);
            throw new ValidationException("Could not extract external reference from Clip webhook payload.");
        }

        var paymentStatus = ExtractPaymentStatus(root);

        if (paymentStatus == PaymentStatus.Completed)
        {
            var payment = await _orderService.ConfirmPaymentByExternalIdAsync(externalTransactionId);
            _logger.LogInformation(
                "Clip payment confirmed: {ExternalId} → OrderPayment {PaymentId}",
                externalTransactionId, payment.Id);
        }
        else
        {
            _logger.LogInformation(
                "Clip webhook {EventId}: payment status is '{Status}', no action taken",
                inboxEvent.ExternalEventId, paymentStatus);
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Extracts the external transaction reference from a Clip webhook payload.
    /// Looks for "reference", "transaction_id", or "data.id".
    /// </summary>
    private static string? ExtractExternalReference(JsonElement root)
    {
        if (root.TryGetProperty("reference", out var refProp))
            return refProp.GetString();

        if (root.TryGetProperty("transaction_id", out var txnId))
            return txnId.GetString();

        if (root.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var dataId))
            return dataId.ToString();

        return null;
    }

    /// <summary>
    /// Maps Clip webhook status to internal PaymentStatus.
    /// </summary>
    private static string ExtractPaymentStatus(JsonElement root)
    {
        string? clipStatus = null;

        if (root.TryGetProperty("status", out var statusProp))
            clipStatus = statusProp.GetString();
        else if (root.TryGetProperty("data", out var data) && data.TryGetProperty("status", out var dataStatus))
            clipStatus = dataStatus.GetString();

        return clipStatus?.ToLowerInvariant() switch
        {
            "approved" or "completed" or "paid" => PaymentStatus.Completed,
            "rejected" or "declined" => PaymentStatus.Failed,
            "refunded" => PaymentStatus.Refunded,
            _ => PaymentStatus.Pending
        };
    }

    #endregion
}
