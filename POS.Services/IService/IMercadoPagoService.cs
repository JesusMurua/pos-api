using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for MercadoPago payment integration.
/// </summary>
public interface IMercadoPagoService
{
    /// <summary>
    /// Creates a payment intent on MercadoPago and registers a pending payment on the order.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="orderId">The order UUID.</param>
    /// <param name="amountCents">The amount in cents.</param>
    /// <returns>The external transaction ID and init point URL.</returns>
    Task<PaymentIntentResult> CreatePaymentIntentAsync(int branchId, string orderId, int amountCents);

    /// <summary>
    /// Processes a webhook event from the PaymentWebhookInbox.
    /// Finds the matching OrderPayment and confirms it.
    /// </summary>
    Task ProcessWebhookAsync(PaymentWebhookInbox inboxEvent);
}

public class PaymentIntentResult
{
    public string ExternalTransactionId { get; set; } = null!;
    public string? InitPoint { get; set; }
    public string Status { get; set; } = null!;
}
