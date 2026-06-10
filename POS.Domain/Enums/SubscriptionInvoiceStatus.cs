namespace POS.Domain.Enums;

/// <summary>
/// Lifecycle of a SaaS <see cref="Models.SubscriptionInvoice"/> (tenant → operator).
/// Distinct from <see cref="InvoiceStatus"/>, which is the tenant's CFDI to its own
/// customer. Persisted as a string (<c>HasConversion&lt;string&gt;</c>) so the DB/wire
/// value is the stable name. <c>Refunded</c> is a placeholder (refunds are deferred — OQ-9).
/// </summary>
public enum SubscriptionInvoiceStatus
{
    Open,
    PartiallyPaid,
    Paid,
    Overdue,
    Void,
    Refunded
}
