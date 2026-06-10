namespace POS.Domain.Enums;

/// <summary>
/// Kind of line on a <see cref="Models.SubscriptionInvoiceItem"/>. <c>Discount</c> lines
/// carry a negative <c>TotalAmountCents</c>. <c>AddOn</c> is not produced until PR-4.
/// Persisted as a string (<c>HasConversion&lt;string&gt;</c>).
/// </summary>
public enum SubscriptionInvoiceItemType
{
    PlanBase,
    AddOn,
    Discount,
    Adjustment
}
