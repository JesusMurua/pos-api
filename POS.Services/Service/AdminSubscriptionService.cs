using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using POS.Domain.DTOs.Admin;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <inheritdoc />
public class AdminSubscriptionService : IAdminSubscriptionService
{
    private readonly ApplicationDbContext _context;
    private readonly IStripeService _stripe;
    private readonly IBusinessAuditService _audit;
    private readonly IFeatureGateService _featureGate;
    private readonly ILogger<AdminSubscriptionService> _logger;

    public AdminSubscriptionService(
        ApplicationDbContext context,
        IStripeService stripe,
        IBusinessAuditService audit,
        IFeatureGateService featureGate,
        ILogger<AdminSubscriptionService> logger)
    {
        _context = context;
        _stripe = stripe;
        _audit = audit;
        _featureGate = featureGate;
        _logger = logger;
    }

    public async Task<AdminSubscriptionDetailDto> GetAsync(int businessId)
    {
        var sub = await _context.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.BusinessId == businessId)
            ?? throw new NotFoundException($"Business {businessId} has no subscription.");

        var railCode = sub.BillingMethodId == null ? null : await _context.SaaSBillingMethods
            .Where(m => m.Id == sub.BillingMethodId).Select(m => m.Code).FirstOrDefaultAsync();

        var history = await _context.SubscriptionPriceHistories.AsNoTracking()
            .Where(h => h.SubscriptionId == sub.Id)
            .OrderByDescending(h => h.Id)
            .Select(h => new SubscriptionPriceHistoryDto(
                h.Id, h.BeforeAmountCents, h.AfterAmountCents, h.ChangedAtUtc,
                h.ChangedByTokenId, h.Reason, h.EffectiveDate))
            .ToListAsync();

        return new AdminSubscriptionDetailDto(
            sub.BusinessId, sub.PlanTypeId, PlanTypeIds.ToCode(sub.PlanTypeId),
            sub.BaseAmountCents, sub.Currency, sub.BillingMethodId, railCode,
            sub.Status, sub.BillingCycle, sub.PricingGroup, sub.StripeSubscriptionId,
            sub.StripePriceId, sub.CfdiRequired, sub.BillingEmail, sub.Notes,
            sub.NextBillingDate, history);
    }

    public async Task UpdateAsync(int businessId, AdminUpdateSubscriptionRequest request, string? tokenId)
    {
        var sub = await _context.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.BusinessId == businessId)
            ?? throw new NotFoundException($"Business {businessId} has no subscription to manage.");

        var newPlan = request.PlanTypeId ?? sub.PlanTypeId;
        var newBillingMethodId = request.BillingMethodId ?? sub.BillingMethodId;
        var priceChanged = request.BaseAmountCents.HasValue && request.BaseAmountCents != sub.BaseAmountCents;
        var newAmount = request.BaseAmountCents ?? sub.BaseAmountCents;

        var railCode = newBillingMethodId == null ? null : await _context.SaaSBillingMethods
            .Where(m => m.Id == newBillingMethodId).Select(m => m.Code).FirstOrDefaultAsync();
        var isStripeRail = railCode == "Stripe" && !string.IsNullOrEmpty(sub.StripeSubscriptionId);

        // ── Remote-first: on the Stripe rail a price change hits Stripe BEFORE any
        //    local persist. If Stripe throws, nothing below runs → no partial state.
        string? oldPriceId = null;
        if (priceChanged && isStripeRail)
        {
            if (string.IsNullOrEmpty(sub.StripeBaseItemId))
                throw new ValidationException("Subscription has no Stripe base item to reprice.");

            var newPriceId = await _stripe.CreateCustomPriceAsync(
                newPlan, businessId, newAmount!.Value, sub.Currency, sub.BillingCycle, sub.PricingGroup);
            await _stripe.UpdateSubscriptionPriceAsync(sub.StripeSubscriptionId, sub.StripeBaseItemId, newPriceId);

            oldPriceId = sub.StripePriceId;
            sub.StripePriceId = newPriceId;
        }

        // ── Local persist (one transaction): subscription + price history + audit + Business gate.
        var beforeAmount = sub.BaseAmountCents;
        sub.PlanTypeId = newPlan;
        sub.BaseAmountCents = newAmount;
        sub.BillingMethodId = newBillingMethodId;
        if (request.CfdiRequired.HasValue) sub.CfdiRequired = request.CfdiRequired.Value;
        if (request.BillingEmail != null) sub.BillingEmail = request.BillingEmail;
        if (request.Notes != null) sub.Notes = request.Notes;
        sub.UpdatedAt = DateTime.UtcNow;

        if (priceChanged)
        {
            _context.SubscriptionPriceHistories.Add(new SubscriptionPriceHistory
            {
                SubscriptionId = sub.Id,
                BeforeAmountCents = beforeAmount,
                AfterAmountCents = newAmount!.Value,
                ChangedAtUtc = DateTime.UtcNow,
                ChangedByTokenId = tokenId,
                Reason = request.Reason ?? "Admin price change",
                EffectiveDate = DateTime.UtcNow
            });
        }

        var business = await _context.Businesses.IgnoreQueryFilters().FirstAsync(b => b.Id == businessId);
        business.PlanTypeId = newPlan; // SSoT for the feature gate

        _audit.Record(
            priceChanged ? BusinessAuditAction.SubscriptionPriceChanged : BusinessAuditAction.PlanChanged,
            businessId, request.Reason,
            new { beforeAmount, beforePlan = (int?)null },
            new { afterAmount = newAmount, afterPlan = newPlan },
            tokenId);

        await _context.SaveChangesAsync();
        _featureGate.Invalidate(businessId);

        // Archive the superseded custom Price post-success (best-effort; a failure
        // leaves an orphan Price, not a data inconsistency).
        if (oldPriceId != null)
        {
            try { await _stripe.ArchivePriceAsync(oldPriceId); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to archive superseded Stripe price {PriceId} for business {BusinessId}",
                    oldPriceId, businessId);
            }
        }
    }

    public async Task ChangePlanAsync(int businessId, int planTypeId, string? reason, string? tokenId)
    {
        var business = await _context.Businesses.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.Id == businessId)
            ?? throw new NotFoundException($"Business {businessId} not found.");

        var before = business.PlanTypeId;
        business.PlanTypeId = planTypeId;

        // Keep the denormalized Subscription.PlanTypeId in sync when one exists.
        var sub = await _context.Subscriptions.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.BusinessId == businessId);
        if (sub != null) sub.PlanTypeId = planTypeId;

        _audit.Record(BusinessAuditAction.PlanChanged, businessId, reason,
            new { before }, new { after = planTypeId }, tokenId);

        await _context.SaveChangesAsync();
        _featureGate.Invalidate(businessId);
    }
}
