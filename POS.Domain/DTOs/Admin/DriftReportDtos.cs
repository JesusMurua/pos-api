using POS.Domain.Enums;

namespace POS.Domain.DTOs.Admin;

/// <summary>
/// One flagged payment in the drift report: a synced payment whose method was
/// unknown (recorded as Other) or not authorized by the tenant's plan.
/// </summary>
public sealed record DriftPaymentEntryDto(
    string OrderId,
    int OrderNumber,
    int BusinessId,
    string BusinessName,
    string PlanType,
    string MethodCode,
    string MethodName,
    PaymentCategory MethodCategory,
    bool WasUnauthorized,
    bool WasUnknownMethod,
    DateTime CreatedAt,
    int AmountCents);

/// <summary>Paged drift-report response.</summary>
public sealed record PagedDriftReportDto(
    int Page,
    int PageSize,
    int TotalRows,
    IReadOnlyList<DriftPaymentEntryDto> Items);
