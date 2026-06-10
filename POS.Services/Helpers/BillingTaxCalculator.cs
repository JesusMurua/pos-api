namespace POS.Services.Helpers;

/// <summary>
/// Backend IVA computation for SaaS <c>SubscriptionInvoice</c>s on manual rails
/// (OQ-8 — chosen over Stripe Tax to keep one tax engine). Line items are net (tax
/// exclusive): subtotal is their sum, tax = subtotal × rate, total = subtotal + tax.
/// Discount lines (negative totals) are already in the subtotal, so IVA applies
/// post-discount.
///
/// IMPORTANT: this runs ONLY for manual-rail, job-generated invoices. Stripe-rail
/// invoices are mirrored from the webhook with Stripe's own amounts and must never
/// pass through here (Stripe already computed tax) — see
/// <c>StripeEventProcessorWorker.HandleInvoicePaymentSucceededAsync</c>.
/// </summary>
public static class BillingTaxCalculator
{
    /// <summary>
    /// Computes (subtotal, tax, total) in cents from a net subtotal. For currencies
    /// other than MXN the rate falls back to 0 — multi-currency tax is deferred (OQ-10,
    /// explicit debt) so we never invent a foreign tax rate.
    /// </summary>
    public static (int Subtotal, int Tax, int Total) Compute(
        int subtotalCents, string currency, decimal mxVatRate)
    {
        var rate = string.Equals(currency, "MXN", StringComparison.OrdinalIgnoreCase)
            ? mxVatRate
            : 0m; // OQ-10: foreign-currency tax is explicit debt — no rate assumed.

        var tax = (int)Math.Round(subtotalCents * rate, MidpointRounding.AwayFromZero);
        return (subtotalCents, tax, subtotalCents + tax);
    }
}
