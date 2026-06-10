namespace POS.Services.IService;

/// <summary>
/// Records / removes payments against a <c>SubscriptionInvoice</c> and recomputes its
/// status write-time (§8). <see cref="RecordAsync"/> is reused by the Stripe worker to
/// mirror automatic payments, so it is rail-agnostic and idempotent.
/// </summary>
public interface IAdminTenantPaymentService
{
    /// <summary>
    /// Records a payment and recomputes the invoice status in the same transaction.
    /// Idempotent: when <paramref name="reference"/> is present and a payment already
    /// exists for <c>(billingMethodId, reference)</c>, it is a no-op returning false
    /// (pre-check, not exception-catch — the worker keeps using the same DbContext, so a
    /// poisoned context from a caught DbUpdateException is not acceptable).
    ///
    /// Does its OWN SaveChanges so the payment insert + status recompute are atomic
    /// (§8 write-time). Do NOT "optimize" by deferring the save to the caller in the
    /// worker — that would break the recompute atomicity if a later event in the batch fails.
    /// </summary>
    /// <returns>true if a payment was recorded; false if skipped as a duplicate.</returns>
    Task<bool> RecordAsync(
        int invoiceId,
        int billingMethodId,
        int amountCents,
        string currency,
        DateTime paidAtUtc,
        string? reference,
        string? notes,
        string? receivedByTokenIdHash,
        string? stripeChargeId,
        string? rawWebhookPayloadJson);

    /// <summary>Deletes a payment (capture-error fix) and recomputes the invoice status.</summary>
    Task DeleteAsync(int invoiceId, int paymentId, string? tokenId);
}
