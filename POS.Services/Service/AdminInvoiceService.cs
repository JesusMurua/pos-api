using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using POS.Domain.DTOs.Admin;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.Helpers;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class AdminInvoiceService : IAdminInvoiceService
{
    private readonly ApplicationDbContext _context;
    private readonly IUnitOfWork _uow; // shares the scoped DbContext — used only for the atomic invoice counter.
    private readonly IBusinessAuditService _audit;
    private readonly INotificationService _notifications;
    private readonly IConfiguration _configuration;

    public AdminInvoiceService(
        ApplicationDbContext context,
        IUnitOfWork uow,
        IBusinessAuditService audit,
        INotificationService notifications,
        IConfiguration configuration)
    {
        _context = context;
        _uow = uow;
        _audit = audit;
        _notifications = notifications;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<AdminInvoiceListItemDto>> GetForBusinessAsync(int businessId)
    {
        return await _context.SubscriptionInvoices.AsNoTracking()
            .Where(i => i.BusinessId == businessId)
            .OrderByDescending(i => i.InvoiceNumber)
            .Select(i => new AdminInvoiceListItemDto(
                i.Id, i.InvoiceNumber, i.Status.ToString(), i.IssuedAtUtc, i.DueDate,
                i.PeriodStart, i.PeriodEnd, i.SubtotalCents, i.TaxCents, i.TotalCents,
                i.Payments.Sum(p => p.AmountCents), i.Currency, i.StripeInvoiceId))
            .ToListAsync();
    }

    public async Task<AdminInvoiceDetailDto> GetAsync(int invoiceId)
    {
        var invoice = await _context.SubscriptionInvoices.AsNoTracking()
            .Include(i => i.Items)
            .Include(i => i.Payments)
            .FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new NotFoundException($"SubscriptionInvoice {invoiceId} not found.");

        return MapDetail(invoice);
    }

    public async Task<AdminInvoiceDetailDto> CreateAsync(
        int businessId, AdminCreateInvoiceRequest request, string? tokenId)
    {
        var sub = await _context.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.BusinessId == businessId)
            ?? throw new NotFoundException($"Business {businessId} has no subscription to invoice.");

        if (request.Items.Count == 0)
            throw new ValidationException("An invoice must have at least one item.");

        var now = DateTime.UtcNow;
        var periodStart = request.PeriodStart ?? now;
        var periodEnd = request.PeriodEnd ?? AdvancePeriod(periodStart, sub.BillingCycle);
        var dueDate = request.DueDate ?? now.AddDays(7);

        var items = request.Items.Select(BuildItem).ToList();
        var subtotal = items.Sum(it => it.TotalAmountCents);
        var (subtotalCents, taxCents, totalCents) =
            BillingTaxCalculator.Compute(subtotal, sub.Currency, MxVatRate);

        var invoiceNumber = await _uow.Business.IncrementInvoiceCounterAsync(businessId);

        var invoice = new SubscriptionInvoice
        {
            SubscriptionId = sub.Id,
            BusinessId = businessId,
            InvoiceNumber = invoiceNumber,
            // A zero-total (e.g. full-discount) invoice is Paid on creation (§13).
            Status = totalCents <= 0 ? SubscriptionInvoiceStatus.Paid : SubscriptionInvoiceStatus.Open,
            IssuedAtUtc = now,
            DueDate = dueDate,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            SubtotalCents = subtotalCents,
            TaxCents = taxCents,
            TotalCents = totalCents,
            Currency = sub.Currency,
            CreatedByTokenIdHash = tokenId,
            Items = items
        };
        FreezeCfdiReceptor(invoice, sub, businessId);

        _context.SubscriptionInvoices.Add(invoice);

        _audit.Record(BusinessAuditAction.InvoiceCreated, businessId, request.Reason,
            before: null, after: new { invoiceNumber, totalCents }, tokenId);

        await _notifications.EnqueueAsync("InvoiceCreated", NotificationRecipientType.BillingEmail, businessId,
            new Dictionary<string, string>
            {
                ["invoiceNumber"] = invoiceNumber.ToString(),
                ["totalPesos"] = $"${totalCents / 100m:N2}",
                ["dueDate"] = dueDate.ToString("yyyy-MM-dd")
            });

        await _context.SaveChangesAsync();
        return MapDetail(invoice);
    }

    public async Task UpdateAsync(int invoiceId, AdminUpdateInvoiceRequest request, string? tokenId)
    {
        var invoice = await _context.SubscriptionInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new NotFoundException($"SubscriptionInvoice {invoiceId} not found.");

        if (invoice.Status != SubscriptionInvoiceStatus.Open)
            throw new ValidationException($"Only Open invoices can be edited (current: {invoice.Status}).");

        if (request.DueDate.HasValue) invoice.DueDate = request.DueDate.Value;

        await _context.SaveChangesAsync();
    }

    public async Task VoidAsync(int invoiceId, string? reason, string? tokenId)
    {
        var invoice = await _context.SubscriptionInvoices.FirstOrDefaultAsync(i => i.Id == invoiceId)
            ?? throw new NotFoundException($"SubscriptionInvoice {invoiceId} not found.");

        // Void only from {Open, Overdue}. A PartiallyPaid invoice holds recorded money;
        // delete its payments first (DELETE …/payments/{id}). Refunds are deferred (OQ-9).
        if (invoice.Status is not (SubscriptionInvoiceStatus.Open or SubscriptionInvoiceStatus.Overdue))
            throw new ValidationException(
                $"Cannot void a {invoice.Status} invoice. Delete its payments first if PartiallyPaid.");

        invoice.Status = SubscriptionInvoiceStatus.Void;

        _audit.Record(BusinessAuditAction.InvoiceVoided, invoice.BusinessId, reason,
            before: new { invoice.InvoiceNumber }, after: new { status = "Void" }, tokenId);

        await _context.SaveChangesAsync();
    }

    #region Helpers

    private decimal MxVatRate => _configuration.GetValue<decimal?>("Billing:MxVatRate") ?? 0.16m;

    private static SubscriptionInvoiceItem BuildItem(AdminCreateInvoiceItemRequest r)
    {
        if (!Enum.TryParse<SubscriptionInvoiceItemType>(r.ItemType, ignoreCase: true, out var type))
            throw new ValidationException($"Invalid invoice item type '{r.ItemType}'.");

        return new SubscriptionInvoiceItem
        {
            Description = r.Description,
            Quantity = r.Quantity,
            UnitAmountCents = r.UnitAmountCents,
            TotalAmountCents = r.UnitAmountCents * r.Quantity,
            ItemType = type,
            LinkedAddOnId = r.LinkedAddOnId,
            LinkedPlanTypeId = r.LinkedPlanTypeId
        };
    }

    /// <summary>
    /// Freezes the CFDI receptor block from Business at issue time (M1) when the
    /// subscription opted into CFDI. SAT stamping fields stay null until PR-7.
    /// </summary>
    private void FreezeCfdiReceptor(SubscriptionInvoice invoice, Subscription sub, int businessId)
    {
        if (!sub.CfdiRequired) return;

        var biz = _context.Businesses.IgnoreQueryFilters().First(b => b.Id == businessId);
        invoice.ReceptorRfc = biz.Rfc;
        invoice.ReceptorRegime = biz.TaxRegime;
        invoice.ReceptorLegalName = biz.LegalName;
    }

    private static DateTime AdvancePeriod(DateTime from, string billingCycle) =>
        string.Equals(billingCycle, "Annual", StringComparison.OrdinalIgnoreCase)
            ? from.AddYears(1)
            : from.AddMonths(1);

    private static AdminInvoiceDetailDto MapDetail(SubscriptionInvoice i) => new(
        i.Id, i.SubscriptionId, i.BusinessId, i.InvoiceNumber, i.Status.ToString(),
        i.IssuedAtUtc, i.DueDate, i.PeriodStart, i.PeriodEnd,
        i.SubtotalCents, i.TaxCents, i.TotalCents,
        i.Payments.Sum(p => p.AmountCents), i.Currency, i.StripeInvoiceId,
        i.Items.Select(it => new AdminInvoiceItemDto(
            it.Id, it.Description, it.Quantity, it.UnitAmountCents, it.TotalAmountCents,
            it.ItemType.ToString(), it.LinkedAddOnId, it.LinkedPlanTypeId)).ToList(),
        i.Payments.Select(p => new AdminTenantPaymentDto(
            p.Id, p.BillingMethodId, p.AmountCents, p.Currency, p.PaidAtUtc,
            p.Reference, p.Notes, p.ReceivedByTokenIdHash == null, p.StripeChargeId)).ToList());

    #endregion
}
