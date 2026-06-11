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
    private readonly INotificationService _notifications;
    private readonly IFeatureGateService _featureGate;
    private readonly ILogger<AdminSubscriptionService> _logger;

    public AdminSubscriptionService(
        ApplicationDbContext context,
        IStripeService stripe,
        IBusinessAuditService audit,
        INotificationService notifications,
        IFeatureGateService featureGate,
        ILogger<AdminSubscriptionService> logger)
    {
        _context = context;
        _stripe = stripe;
        _audit = audit;
        _notifications = notifications;
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

        // Active add-ons only (DeactivatedAt IS NULL); deactivated rows are kept for
        // history but never shown on the live subscription. EffectivePriceCents resolves
        // CustomPriceCents ?? DefaultPriceCents server-side.
        // BillingCycle carries a HasConversion<string>, so .ToString() is mapped in memory
        // after materialization (not all providers translate it inside the SQL projection).
        var addOnRows = await (
            from sa in _context.SubscriptionAddOns.AsNoTracking()
            join pa in _context.PlanAddOns.AsNoTracking() on sa.AddOnId equals pa.Id
            where sa.SubscriptionId == sub.Id && sa.DeactivatedAt == null
            orderby sa.ActivatedAt
            select new
            {
                sa.Id, sa.AddOnId, pa.Code, pa.Name, sa.Quantity,
                sa.CustomPriceCents, pa.DefaultPriceCents, pa.BillingCycle, sa.ActivatedAt
            })
            .ToListAsync();

        var addOns = addOnRows.Select(x => new SubscriptionAddOnDto(
            x.Id, x.AddOnId, x.Code, x.Name, x.Quantity,
            x.CustomPriceCents, x.DefaultPriceCents,
            x.CustomPriceCents ?? x.DefaultPriceCents,
            x.BillingCycle.ToString(), x.ActivatedAt)).ToList();

        return new AdminSubscriptionDetailDto(
            sub.BusinessId, sub.PlanTypeId, PlanTypeIds.ToCode(sub.PlanTypeId),
            sub.BaseAmountCents, sub.Currency, sub.BillingMethodId, railCode,
            sub.Status, sub.BillingCycle, sub.PricingGroup, sub.StripeSubscriptionId,
            sub.StripePriceId, sub.CfdiRequired, sub.BillingEmail, sub.Notes,
            sub.NextBillingDate, history, addOns);
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
        var oldPlanCode = PlanTypeIds.ToCode(sub.PlanTypeId); // capture before the mutation below
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

        if (priceChanged)
            await _notifications.EnqueueAsync("SubscriptionPriceChanged", NotificationRecipientType.Owner, businessId,
                new Dictionary<string, string>
                {
                    ["beforePesos"] = $"${(beforeAmount ?? 0) / 100m:N2}",
                    ["afterPesos"] = $"${newAmount!.Value / 100m:N2}",
                    ["effectiveDate"] = DateTime.UtcNow.ToString("yyyy-MM-dd")
                });
        else
            await _notifications.EnqueueAsync("PlanChanged", NotificationRecipientType.Owner, businessId,
                new Dictionary<string, string>
                {
                    ["oldPlan"] = oldPlanCode,
                    ["newPlan"] = PlanTypeIds.ToCode(newPlan)
                });

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

    public async Task ActivateAddOnAsync(int businessId, AdminActivateAddOnRequest request, string? tokenId)
    {
        var sub = await _context.Subscriptions.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.BusinessId == businessId)
            ?? throw new NotFoundException($"Business {businessId} has no subscription.");

        var addOn = await _context.PlanAddOns.FirstOrDefaultAsync(a => a.Id == request.AddOnId && a.IsActive)
            ?? throw new NotFoundException($"Active PlanAddOn {request.AddOnId} not found.");

        // Pre-check the active-uniqueness invariant (the partial unique index is the DB backstop
        // but is not enforced by the InMemory test provider).
        var dup = await _context.SubscriptionAddOns
            .AnyAsync(sa => sa.SubscriptionId == sub.Id && sa.AddOnId == addOn.Id && sa.DeactivatedAt == null);
        if (dup)
            throw new ConcurrencyConflictException(
                $"Add-on {addOn.Code} is already active on this subscription. Deactivate it first.");

        var railCode = sub.BillingMethodId == null ? null : await _context.SaaSBillingMethods
            .Where(m => m.Id == sub.BillingMethodId).Select(m => m.Code).FirstOrDefaultAsync();
        var isStripeRail = railCode == "Stripe" && !string.IsNullOrEmpty(sub.StripeSubscriptionId);

        string? stripeItemId = null;
        string? stripePriceId = null;

        // ── Remote-first on the Stripe rail: resolve/create the Price, append the item,
        //    await 2xx. If Stripe throws, nothing below runs → no partial local state.
        if (isStripeRail)
        {
            if (request.CustomPriceCents.HasValue)
            {
                stripePriceId = await _stripe.CreateAddOnPriceAsync(
                    addOn.Id, businessId, request.CustomPriceCents.Value, sub.Currency, addOn.BillingCycle.ToString());
            }
            else
            {
                stripePriceId = addOn.StripePriceId
                    ?? throw new ValidationException(
                        $"Add-on {addOn.Code} has no catalog Stripe price; a CustomPriceCents is required on the Stripe rail.");
            }

            stripeItemId = await _stripe.AddSubscriptionItemAsync(
                sub.StripeSubscriptionId, stripePriceId, request.Quantity);
        }

        _context.SubscriptionAddOns.Add(new SubscriptionAddOn
        {
            SubscriptionId = sub.Id,
            AddOnId = addOn.Id,
            Quantity = request.Quantity,
            ActivatedAt = DateTime.UtcNow,
            CustomPriceCents = request.CustomPriceCents,
            ActivatedByTokenIdHash = tokenId,
            Reason = request.Reason,
            StripeItemId = stripeItemId,
            StripeAddOnPriceId = stripePriceId
        });
        sub.UpdatedAt = DateTime.UtcNow;

        _audit.Record(BusinessAuditAction.AddOnActivated, businessId, request.Reason,
            before: null,
            after: new { addOnId = addOn.Id, addOn.Code, request.Quantity, request.CustomPriceCents },
            tokenId);

        await _notifications.EnqueueAsync("AddOnActivated", NotificationRecipientType.Owner, businessId,
            new Dictionary<string, string> { ["addOnName"] = addOn.Name, ["quantity"] = request.Quantity.ToString() });

        await _context.SaveChangesAsync();
    }

    public async Task DeactivateAddOnAsync(int businessId, int subscriptionAddOnId, string? tokenId)
    {
        var addOn = await _context.SubscriptionAddOns
            .FirstOrDefaultAsync(sa => sa.Id == subscriptionAddOnId
                                    && sa.Subscription!.BusinessId == businessId
                                    && sa.DeactivatedAt == null)
            ?? throw new NotFoundException($"Active SubscriptionAddOn {subscriptionAddOnId} not found for business {businessId}.");

        var sub = await _context.Subscriptions.IgnoreQueryFilters().FirstAsync(s => s.Id == addOn.SubscriptionId);

        var railCode = sub.BillingMethodId == null ? null : await _context.SaaSBillingMethods
            .Where(m => m.Id == sub.BillingMethodId).Select(m => m.Code).FirstOrDefaultAsync();
        var isStripeRail = railCode == "Stripe" && !string.IsNullOrEmpty(addOn.StripeItemId);

        // Remote-first: drop the Stripe item (with proration), await 2xx, then persist.
        if (isStripeRail)
            await _stripe.RemoveSubscriptionItemAsync(addOn.StripeItemId!);

        addOn.DeactivatedAt = DateTime.UtcNow;
        sub.UpdatedAt = DateTime.UtcNow;

        _audit.Record(BusinessAuditAction.AddOnDeactivated, businessId, null,
            before: new { subscriptionAddOnId, addOn.AddOnId }, after: new { status = "Deactivated" }, tokenId);

        var addOnName = await _context.PlanAddOns.Where(a => a.Id == addOn.AddOnId).Select(a => a.Name).FirstAsync();
        await _notifications.EnqueueAsync("AddOnDeactivated", NotificationRecipientType.Owner, businessId,
            new Dictionary<string, string> { ["addOnName"] = addOnName });

        await _context.SaveChangesAsync();

        // Archive a CUSTOM Stripe Price post-success (catalog prices are shared — never archived).
        if (addOn.CustomPriceCents.HasValue && !string.IsNullOrEmpty(addOn.StripeAddOnPriceId))
        {
            try { await _stripe.ArchivePriceAsync(addOn.StripeAddOnPriceId); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to archive custom add-on price {PriceId} for business {BusinessId}",
                    addOn.StripeAddOnPriceId, businessId);
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

        await _notifications.EnqueueAsync("PlanChanged", NotificationRecipientType.Owner, businessId,
            new Dictionary<string, string>
            {
                ["oldPlan"] = PlanTypeIds.ToCode(before),
                ["newPlan"] = PlanTypeIds.ToCode(planTypeId)
            });

        await _context.SaveChangesAsync();
        _featureGate.Invalidate(businessId);
    }
}
