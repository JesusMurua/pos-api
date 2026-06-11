namespace POS.Domain.DTOs.Admin;

/// <summary>Admin view of a tenant's SaaS subscription + its price history.</summary>
public sealed record AdminSubscriptionDetailDto(
    int BusinessId,
    int PlanTypeId,
    string PlanTypeCode,
    int? BaseAmountCents,
    string Currency,
    int? BillingMethodId,
    string? BillingMethodCode,
    string Status,
    string BillingCycle,
    string PricingGroup,
    string? StripeCustomerId,
    string? StripeSubscriptionId,
    string? StripePriceId,
    bool CfdiRequired,
    string? BillingEmail,
    string? Notes,
    DateTime? NextBillingDate,
    IReadOnlyList<SubscriptionPriceHistoryDto> PriceHistory,
    IReadOnlyList<SubscriptionAddOnDto> ActiveAddOns);

public sealed record SubscriptionPriceHistoryDto(
    int Id,
    int? BeforeAmountCents,
    int AfterAmountCents,
    DateTime ChangedAtUtc,
    string? ChangedByTokenId,
    string Reason,
    DateTime EffectiveDate);

/// <summary>
/// An active add-on on the subscription (only rows with <c>DeactivatedAt IS NULL</c>).
/// <c>EffectivePriceCents = CustomPriceCents ?? DefaultPriceCents</c> is resolved
/// server-side so the UI shows the charged amount without re-deriving it.
/// </summary>
public sealed record SubscriptionAddOnDto(
    int SubscriptionAddOnId,
    int AddOnId,
    string AddOnCode,
    string AddOnName,
    int Quantity,
    int? CustomPriceCents,
    int DefaultPriceCents,
    int EffectivePriceCents,
    string BillingCycle,
    DateTime ActivatedAt);

/// <summary>
/// Admin create payload — provisions a subscription where none exists (the PUT only edits
/// an existing one). <c>PlanTypeId</c> + <c>BillingMethodId</c> are required. On the Stripe
/// rail it creates the Stripe Customer (if absent) + Subscription against the catalog Price
/// for (plan, Monthly, business pricing group), remote-first; on a manual rail it persists
/// locally only. Records a <c>SubscriptionCreated</c> BusinessAuditLog row.
/// </summary>
public sealed record AdminCreateSubscriptionRequest
{
    /// <summary>Required. FK → PlanTypeCatalog (1=Free, 2=Basic, 3=Pro, 4=Enterprise).</summary>
    public int PlanTypeId { get; init; }

    /// <summary>Required. FK → SaaSBillingMethod (the rail charging this tenant).</summary>
    public int BillingMethodId { get; init; }

    /// <summary>Negotiated per-tenant price (cents). Null = unset (e.g. Enterprise without a price).</summary>
    public int? BaseAmountCents { get; init; }

    public string Currency { get; init; } = "MXN";
    public bool CfdiRequired { get; init; }
    public string? BillingEmail { get; init; }
    public string? Notes { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Admin reconcile payload. Only supplied fields change — a null/absent field is a no-op,
/// never a reset (e.g. <c>BaseAmountCents</c> is PRESERVED across a <c>PlanTypeId</c> change;
/// to move it to the new plan's default the caller passes the explicit value). A
/// <c>BaseAmountCents</c> change on the Stripe rail creates a dynamic Price + updates Stripe
/// (remote-first); on a manual rail it only updates local state. Records SubscriptionPriceHistory +
/// BusinessAuditLog.
/// </summary>
public sealed record AdminUpdateSubscriptionRequest
{
    public int? PlanTypeId { get; init; }
    public int? BaseAmountCents { get; init; }
    public int? BillingMethodId { get; init; }
    public bool? CfdiRequired { get; init; }
    public string? BillingEmail { get; init; }
    public string? Notes { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Activate an add-on on a subscription. On the Stripe rail a <c>CustomPriceCents</c> creates
/// a dynamic add-on Price; otherwise the catalog <c>PlanAddOn.StripePriceId</c> is reused.
/// </summary>
public sealed record AdminActivateAddOnRequest
{
    public int AddOnId { get; init; }
    public int Quantity { get; init; } = 1;
    public int? CustomPriceCents { get; init; }
    public string? Reason { get; init; }
}
