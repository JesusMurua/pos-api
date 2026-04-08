using System.Text.Json;
using Microsoft.Extensions.Logging;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class MercadoPagoService : IMercadoPagoService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IOrderService _orderService;
    private readonly HttpClient _httpClient;
    private readonly ILogger<MercadoPagoService> _logger;

    public MercadoPagoService(
        IUnitOfWork unitOfWork,
        IOrderService orderService,
        HttpClient httpClient,
        ILogger<MercadoPagoService> logger)
    {
        _unitOfWork = unitOfWork;
        _orderService = orderService;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Creates a payment intent on MercadoPago and registers a pending payment on the order.
    /// Currently uses a placeholder — real MercadoPago API integration will replace the HTTP call.
    /// </summary>
    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(int branchId, string orderId, int amountCents)
    {
        var config = await _unitOfWork.BranchPaymentConfigs
            .GetByBranchAndProviderAsync(branchId, "mercadopago")
            ?? throw new ValidationException(
                "MercadoPago is not configured for this branch. Add a payment config first.");

        if (!config.IsActive)
            throw new ValidationException("MercadoPago integration is disabled for this branch.");

        // ── Placeholder: MercadoPago API call ──
        // In production, this would POST to MercadoPago's API to create a payment preference
        // using config.AccessToken as the Bearer token.
        // For now, generate a synthetic external transaction ID.
        var externalTransactionId = $"mp_intent_{Guid.NewGuid():N}";
        string? initPoint = $"https://www.mercadopago.com.mx/checkout/v1/redirect?pref_id={externalTransactionId}";

        _logger.LogInformation(
            "MercadoPago payment intent created for order {OrderId}: {ExternalId} ({AmountCents} cents)",
            orderId, externalTransactionId, amountCents);

        // Register a pending payment on the order
        var payment = new OrderPayment
        {
            Method = PaymentMethod.MercadoPago,
            AmountCents = amountCents,
            PaymentProvider = "mercadopago",
            ExternalTransactionId = externalTransactionId,
            PaymentStatusId = PaymentStatus.Pending
        };

        await _orderService.AddPaymentAsync(orderId, branchId, payment);

        return new PaymentIntentResult
        {
            ExternalTransactionId = externalTransactionId,
            InitPoint = initPoint,
            Status = "pending"
        };
    }

    /// <summary>
    /// Processes a webhook event from the PaymentWebhookInbox.
    /// Parses the payload, extracts the external reference, and confirms the payment.
    /// </summary>
    public async Task ProcessWebhookAsync(PaymentWebhookInbox inboxEvent)
    {
        using var doc = JsonDocument.Parse(inboxEvent.RawPayload);
        var root = doc.RootElement;

        // MercadoPago webhook payloads vary by event type.
        // Common pattern: { "type": "payment", "data": { "id": "..." } }
        // or { "action": "payment.created", "data": { "id": "..." } }
        var externalTransactionId = ExtractExternalReference(root);
        if (string.IsNullOrEmpty(externalTransactionId))
        {
            _logger.LogWarning(
                "MercadoPago webhook {EventId}: could not extract external reference from payload",
                inboxEvent.ExternalEventId);
            throw new ValidationException("Could not extract external reference from MercadoPago webhook payload.");
        }

        var paymentStatus = ExtractPaymentStatus(root);

        if (paymentStatus == PaymentStatus.Completed)
        {
            var payment = await _orderService.ConfirmPaymentByExternalIdAsync(externalTransactionId);
            _logger.LogInformation(
                "MercadoPago payment confirmed: {ExternalId} → OrderPayment {PaymentId}",
                externalTransactionId, payment.Id);
        }
        else
        {
            _logger.LogInformation(
                "MercadoPago webhook {EventId}: payment status is '{Status}', no action taken",
                inboxEvent.ExternalEventId, paymentStatus);
        }
    }

    #region Private Helper Methods

    /// <summary>
    /// Extracts the external transaction reference from a MercadoPago webhook payload.
    /// Looks for "external_reference", "data.id", or "id" in that order.
    /// </summary>
    private static string? ExtractExternalReference(JsonElement root)
    {
        if (root.TryGetProperty("external_reference", out var extRef))
            return extRef.GetString();

        if (root.TryGetProperty("data", out var data) && data.TryGetProperty("id", out var dataId))
            return dataId.ToString();

        return null;
    }

    /// <summary>
    /// Maps MercadoPago webhook status to internal PaymentStatus catalog Id.
    /// </summary>
    private static int ExtractPaymentStatus(JsonElement root)
    {
        string? mpStatus = null;

        if (root.TryGetProperty("status", out var statusProp))
            mpStatus = statusProp.GetString();
        else if (root.TryGetProperty("data", out var data) && data.TryGetProperty("status", out var dataStatus))
            mpStatus = dataStatus.GetString();

        return mpStatus?.ToLowerInvariant() switch
        {
            "approved" => PaymentStatus.Completed,
            "rejected" => PaymentStatus.Failed,
            "refunded" => PaymentStatus.Refunded,
            _ => PaymentStatus.Pending
        };
    }

    #endregion
}
