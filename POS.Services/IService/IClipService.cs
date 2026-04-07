using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for Clip terminal payment integration.
/// </summary>
public interface IClipService
{
    /// <summary>
    /// Creates a payment intent on Clip and registers a pending payment on the order.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="orderId">The order UUID.</param>
    /// <param name="amountCents">The amount in cents.</param>
    /// <returns>The external transaction ID and status.</returns>
    Task<PaymentIntentResult> CreatePaymentIntentAsync(int branchId, string orderId, int amountCents);

    /// <summary>
    /// Processes a webhook event from the PaymentWebhookInbox.
    /// Finds the matching OrderPayment and confirms it.
    /// </summary>
    Task ProcessWebhookAsync(PaymentWebhookInbox inboxEvent);
}
