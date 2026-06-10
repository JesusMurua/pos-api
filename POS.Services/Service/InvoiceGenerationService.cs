using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.Helpers;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class InvoiceGenerationService : IInvoiceGenerationService
{
    private readonly ApplicationDbContext _context;
    private readonly IUnitOfWork _uow; // shares the scoped DbContext — used only for the atomic invoice counter.
    private readonly IBusinessAuditService _audit;
    private readonly IConfiguration _configuration;
    private readonly ILogger<InvoiceGenerationService> _logger;

    private static readonly string[] BillableStatuses = { "active", "past_due" };

    public InvoiceGenerationService(
        ApplicationDbContext context,
        IUnitOfWork uow,
        IBusinessAuditService audit,
        IConfiguration configuration,
        ILogger<InvoiceGenerationService> logger)
    {
        _context = context;
        _uow = uow;
        _audit = audit;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<int> GenerateDueInvoicesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Stripe-rail subscriptions are skipped: Stripe auto-generates their invoices and
        // the worker mirrors them (Stripe SSoT, §2 model C). This job is MANUAL rails only,
        // and manual rails are where the backend computes IVA — Stripe-rail invoices NEVER
        // pass through here, so the tax is never double-applied.
        var stripeRailId = await _context.SaaSBillingMethods
            .Where(m => m.Code == "Stripe").Select(m => (int?)m.Id).FirstOrDefaultAsync(ct);

        var due = await _context.Subscriptions.IgnoreQueryFilters()
            .Where(s => s.NextBillingDate != null
                        && s.NextBillingDate <= now
                        && BillableStatuses.Contains(s.Status)
                        && s.BillingMethodId != stripeRailId
                        && s.BaseAmountCents != null)
            .ToListAsync(ct);

        var generated = 0;
        foreach (var sub in due)
        {
            ct.ThrowIfCancellationRequested();
            if (await GenerateForSubscriptionAsync(sub, now, ct)) generated++;
        }

        if (generated > 0) _logger.LogInformation("InvoiceGeneration: created {Count} manual-rail invoices", generated);
        return generated;
    }

    private async Task<bool> GenerateForSubscriptionAsync(Subscription sub, DateTime now, CancellationToken ct)
    {
        var periodStart = sub.NextBillingDate!.Value;
        var periodEnd = AdvancePeriod(periodStart, sub.BillingCycle);

        // Idempotency by PRE-CHECK (the partial unique (SubscriptionId, PeriodStart)
        // WHERE StripeInvoiceId IS NULL is the backstop): never generate twice for a period.
        var exists = await _context.SubscriptionInvoices
            .AnyAsync(i => i.SubscriptionId == sub.Id
                           && i.PeriodStart == periodStart
                           && i.StripeInvoiceId == null, ct);
        if (exists)
        {
            // Still advance NextBillingDate so a stuck date does not re-trigger every run.
            sub.NextBillingDate = periodEnd;
            await _context.SaveChangesAsync(ct);
            return false;
        }

        var items = new List<SubscriptionInvoiceItem>
        {
            new()
            {
                Description = $"Plan base — {sub.BillingCycle}",
                Quantity = 1,
                UnitAmountCents = sub.BaseAmountCents!.Value,
                TotalAmountCents = sub.BaseAmountCents.Value,
                ItemType = SubscriptionInvoiceItemType.PlanBase,
                LinkedPlanTypeId = sub.PlanTypeId
            }
        };

        // Unapplied price-history rows become one-time adjustment/discount lines, each
        // consumed exactly once (AppliedToInvoiceId set below after the invoice has an Id).
        var pendingHistory = await _context.SubscriptionPriceHistories
            .Where(h => h.SubscriptionId == sub.Id && h.AppliedToInvoiceId == null)
            .ToListAsync(ct);

        foreach (var h in pendingHistory)
        {
            var delta = h.AfterAmountCents - (h.BeforeAmountCents ?? h.AfterAmountCents);
            if (delta == 0) continue;
            items.Add(new SubscriptionInvoiceItem
            {
                Description = $"Adjustment — {h.Reason}",
                Quantity = 1,
                UnitAmountCents = delta,
                TotalAmountCents = delta,
                ItemType = delta < 0 ? SubscriptionInvoiceItemType.Discount : SubscriptionInvoiceItemType.Adjustment
            });
        }

        var subtotal = items.Sum(it => it.TotalAmountCents);
        var (subtotalCents, taxCents, totalCents) =
            BillingTaxCalculator.Compute(subtotal, sub.Currency, MxVatRate);

        var invoiceNumber = await _uow.Business.IncrementInvoiceCounterAsync(sub.BusinessId);

        var invoice = new SubscriptionInvoice
        {
            SubscriptionId = sub.Id,
            BusinessId = sub.BusinessId,
            InvoiceNumber = invoiceNumber,
            Status = totalCents <= 0 ? SubscriptionInvoiceStatus.Paid : SubscriptionInvoiceStatus.Open,
            IssuedAtUtc = now,
            DueDate = now.AddDays(7),
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            SubtotalCents = subtotalCents,
            TaxCents = taxCents,
            TotalCents = totalCents,
            Currency = sub.Currency,
            CreatedByTokenIdHash = null, // generated by the job
            Items = items
        };
        FreezeCfdiReceptor(invoice, sub);

        _context.SubscriptionInvoices.Add(invoice);
        await _context.SaveChangesAsync(ct); // assigns invoice.Id

        foreach (var h in pendingHistory) h.AppliedToInvoiceId = invoice.Id;
        sub.NextBillingDate = periodEnd;

        _audit.Record(BusinessAuditAction.InvoiceCreated, sub.BusinessId, null,
            before: null, after: new { invoiceNumber, totalCents, source = "job" }, tokenId: null);

        await _context.SaveChangesAsync(ct);
        return true;
    }

    public async Task<int> SweepOverdueAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var stale = await _context.SubscriptionInvoices
            .Where(i => (i.Status == SubscriptionInvoiceStatus.Open
                         || i.Status == SubscriptionInvoiceStatus.PartiallyPaid)
                        && i.DueDate < now)
            .ToListAsync(ct);

        foreach (var i in stale) i.Status = SubscriptionInvoiceStatus.Overdue;

        if (stale.Count > 0)
        {
            await _context.SaveChangesAsync(ct);
            _logger.LogInformation("InvoiceGeneration: marked {Count} invoices overdue", stale.Count);
        }
        return stale.Count;
    }

    #region Helpers

    private decimal MxVatRate => _configuration.GetValue<decimal?>("Billing:MxVatRate") ?? 0.16m;

    private void FreezeCfdiReceptor(SubscriptionInvoice invoice, Subscription sub)
    {
        if (!sub.CfdiRequired) return;
        var biz = _context.Businesses.IgnoreQueryFilters().First(b => b.Id == sub.BusinessId);
        invoice.ReceptorRfc = biz.Rfc;
        invoice.ReceptorRegime = biz.TaxRegime;
        invoice.ReceptorLegalName = biz.LegalName;
    }

    private static DateTime AdvancePeriod(DateTime from, string billingCycle) =>
        string.Equals(billingCycle, "Annual", StringComparison.OrdinalIgnoreCase)
            ? from.AddYears(1)
            : from.AddMonths(1);

    #endregion
}
