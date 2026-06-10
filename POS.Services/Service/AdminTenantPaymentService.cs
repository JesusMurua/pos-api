using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class AdminTenantPaymentService : IAdminTenantPaymentService
{
    private readonly ApplicationDbContext _context;
    private readonly IBusinessAuditService _audit;
    private readonly ILogger<AdminTenantPaymentService> _logger;

    public AdminTenantPaymentService(
        ApplicationDbContext context,
        IBusinessAuditService audit,
        ILogger<AdminTenantPaymentService> logger)
    {
        _context = context;
        _audit = audit;
        _logger = logger;
    }

    public async Task<bool> RecordAsync(
        int invoiceId,
        int billingMethodId,
        int amountCents,
        string currency,
        DateTime paidAtUtc,
        string? reference,
        string? notes,
        string? receivedByTokenIdHash,
        string? stripeChargeId,
        string? rawWebhookPayloadJson)
    {
        var invoice = await _context.SubscriptionInvoices
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new NotFoundException($"SubscriptionInvoice {invoiceId} not found.");

        if (invoice.Status is SubscriptionInvoiceStatus.Void or SubscriptionInvoiceStatus.Refunded)
            throw new ValidationException($"Cannot record a payment on a {invoice.Status} invoice.");

        // Currency must match the invoice (v2 is mono-currency; conversion is OQ-10).
        if (!string.Equals(currency, invoice.Currency, StringComparison.OrdinalIgnoreCase))
            throw new ValidationException(
                $"Payment currency {currency} does not match invoice currency {invoice.Currency}.");

        // Idempotency (M4) by PRE-CHECK, never by catching DbUpdateException: this method
        // runs inside the Stripe worker's event loop, which keeps using the same DbContext
        // afterwards — a caught DbUpdateException would poison it and break the rest of the
        // batch. The partial unique index (BillingMethodId, Reference) is only the backstop.
        if (!string.IsNullOrEmpty(reference))
        {
            var dup = await _context.TenantPayments
                .AnyAsync(p => p.BillingMethodId == billingMethodId && p.Reference == reference);
            if (dup)
            {
                _logger.LogInformation(
                    "Duplicate TenantPayment skipped for invoice {InvoiceId} (rail {Rail}, ref {Reference})",
                    invoiceId, billingMethodId, reference);
                return false;
            }
        }

        _context.TenantPayments.Add(new TenantPayment
        {
            InvoiceId = invoiceId,
            BillingMethodId = billingMethodId,
            AmountCents = amountCents,
            Currency = currency,
            PaidAtUtc = paidAtUtc,
            Reference = reference,
            Notes = notes,
            ReceivedByTokenIdHash = receivedByTokenIdHash,
            StripeChargeId = stripeChargeId,
            RawWebhookPayloadJson = rawWebhookPayloadJson
        });

        var paidCents = invoice.Payments.Sum(p => p.AmountCents) + amountCents;
        if (paidCents > invoice.TotalCents)
            _logger.LogWarning(
                "Overpayment on invoice {InvoiceId}: paid {Paid} > total {Total}. Marking Paid; overage is manual.",
                invoiceId, paidCents, invoice.TotalCents);

        invoice.Status = RecomputeStatus(invoice.TotalCents, paidCents);

        _audit.Record(BusinessAuditAction.PaymentRegistered, invoice.BusinessId, notes,
            before: null,
            after: new { invoiceId, amountCents, billingMethodId, automatic = receivedByTokenIdHash == null },
            receivedByTokenIdHash);

        // Own SaveChanges — the insert + status recompute must commit atomically (§8).
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task DeleteAsync(int invoiceId, int paymentId, string? tokenId)
    {
        var invoice = await _context.SubscriptionInvoices
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new NotFoundException($"SubscriptionInvoice {invoiceId} not found.");

        var payment = invoice.Payments.FirstOrDefault(p => p.Id == paymentId)
            ?? throw new NotFoundException($"Payment {paymentId} not found on invoice {invoiceId}.");

        _context.TenantPayments.Remove(payment);

        var paidCents = invoice.Payments.Where(p => p.Id != paymentId).Sum(p => p.AmountCents);
        // Do not resurrect a Void invoice; otherwise recompute from the remaining payments.
        if (invoice.Status != SubscriptionInvoiceStatus.Void)
            invoice.Status = RecomputeStatus(invoice.TotalCents, paidCents);

        _audit.Record(BusinessAuditAction.PaymentDeleted, invoice.BusinessId, null,
            before: new { paymentId, amountCents = payment.AmountCents },
            after: null, tokenId);

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Write-time status (§8): fully covered (incl. overpayment and a zero-total
    /// full-discount invoice) ⇒ Paid; partial ⇒ PartiallyPaid; nothing ⇒ Open.
    /// Never produces Overdue/Void — those are owned by the sweep job / admin void.
    /// </summary>
    private static SubscriptionInvoiceStatus RecomputeStatus(int totalCents, int paidCents)
    {
        if (paidCents >= totalCents) return SubscriptionInvoiceStatus.Paid;
        if (paidCents > 0) return SubscriptionInvoiceStatus.PartiallyPaid;
        return SubscriptionInvoiceStatus.Open;
    }
}
