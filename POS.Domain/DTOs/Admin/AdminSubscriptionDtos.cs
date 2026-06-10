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
    string? StripeSubscriptionId,
    string? StripePriceId,
    bool CfdiRequired,
    string? BillingEmail,
    string? Notes,
    DateTime? NextBillingDate,
    IReadOnlyList<SubscriptionPriceHistoryDto> PriceHistory);

public sealed record SubscriptionPriceHistoryDto(
    int Id,
    int? BeforeAmountCents,
    int AfterAmountCents,
    DateTime ChangedAtUtc,
    string? ChangedByTokenId,
    string Reason,
    DateTime EffectiveDate);

/// <summary>
/// Admin reconcile payload. Only supplied fields change. A <c>BaseAmountCents</c>
/// change on the Stripe rail creates a dynamic Price + updates Stripe (remote-first);
/// on a manual rail it only updates local state. Records SubscriptionPriceHistory +
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
