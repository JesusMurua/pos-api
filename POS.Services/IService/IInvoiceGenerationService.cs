namespace POS.Services.IService;

/// <summary>
/// Generates SaaS invoices for due subscriptions on MANUAL rails (Local SSoT) and sweeps
/// overdue invoices. Driven by the daily <c>InvoiceLifecycleWorker</c>; extracted as a
/// service so the logic is testable without the background loop.
///
/// Stripe-rail subscriptions are deliberately skipped — Stripe auto-generates their
/// invoices and the worker mirrors them (Stripe SSoT, §2 model C).
/// </summary>
public interface IInvoiceGenerationService
{
    /// <summary>Creates an invoice for every manual-rail subscription whose billing period is due.</summary>
    /// <returns>The number of invoices generated.</returns>
    Task<int> GenerateDueInvoicesAsync(CancellationToken ct = default);

    /// <summary>Transitions past-due {Open, PartiallyPaid} invoices to Overdue.</summary>
    /// <returns>The number of invoices marked overdue.</returns>
    Task<int> SweepOverdueAsync(CancellationToken ct = default);
}
