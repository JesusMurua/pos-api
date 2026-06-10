namespace POS.Domain.DTOs.Admin;

/// <summary>One row in the per-business SaaS invoice list.</summary>
public sealed record AdminInvoiceListItemDto(
    int Id,
    int InvoiceNumber,
    string Status,
    DateTime IssuedAtUtc,
    DateTime DueDate,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int SubtotalCents,
    int TaxCents,
    int TotalCents,
    int PaidCents,
    string Currency,
    string? StripeInvoiceId);

/// <summary>Full invoice detail: header + line items + recorded payments.</summary>
public sealed record AdminInvoiceDetailDto(
    int Id,
    int SubscriptionId,
    int BusinessId,
    int InvoiceNumber,
    string Status,
    DateTime IssuedAtUtc,
    DateTime DueDate,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    int SubtotalCents,
    int TaxCents,
    int TotalCents,
    int PaidCents,
    string Currency,
    string? StripeInvoiceId,
    IReadOnlyList<AdminInvoiceItemDto> Items,
    IReadOnlyList<AdminTenantPaymentDto> Payments);

public sealed record AdminInvoiceItemDto(
    int Id,
    string Description,
    int Quantity,
    int UnitAmountCents,
    int TotalAmountCents,
    string ItemType,
    int? LinkedAddOnId,
    int? LinkedPlanTypeId);

public sealed record AdminTenantPaymentDto(
    int Id,
    int BillingMethodId,
    int AmountCents,
    string Currency,
    DateTime PaidAtUtc,
    string? Reference,
    string? Notes,
    bool IsAutomatic,
    string? StripeChargeId);

/// <summary>
/// Create a manual SaaS invoice for a business (manual rails). The backend assigns
/// <c>InvoiceNumber</c>, computes IVA, and opens it <c>Status=Open</c>.
/// </summary>
public sealed record AdminCreateInvoiceRequest
{
    public DateTime? PeriodStart { get; init; }
    public DateTime? PeriodEnd { get; init; }
    public DateTime? DueDate { get; init; }
    public string? Reason { get; init; }
    public List<AdminCreateInvoiceItemRequest> Items { get; init; } = new();
}

public sealed record AdminCreateInvoiceItemRequest
{
    public string Description { get; init; } = null!;
    public int Quantity { get; init; } = 1;
    public int UnitAmountCents { get; init; }
    /// <summary>PlanBase | AddOn | Discount | Adjustment. Discount lines carry a negative total.</summary>
    public string ItemType { get; init; } = "Adjustment";
    public int? LinkedAddOnId { get; init; }
    public int? LinkedPlanTypeId { get; init; }
}

/// <summary>Edit an invoice while it is still <c>Open</c>.</summary>
public sealed record AdminUpdateInvoiceRequest
{
    public DateTime? DueDate { get; init; }
    public string? Reason { get; init; }
}

/// <summary>Void an invoice (audited). Allowed only from {Open, Overdue}.</summary>
public sealed record AdminVoidInvoiceRequest
{
    public string? Reason { get; init; }
}

/// <summary>Record a payment received against an invoice (manual rails).</summary>
public sealed record AdminRecordPaymentRequest
{
    public int BillingMethodId { get; init; }
    public int AmountCents { get; init; }
    public string Currency { get; init; } = "MXN";
    public DateTime? PaidAtUtc { get; init; }
    public string? Reference { get; init; }
    public string? Notes { get; init; }
}
