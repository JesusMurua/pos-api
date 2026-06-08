using POS.Domain.Enums;

namespace POS.Domain.DTOs.Admin;

/// <summary>Full payment-method catalog row for the admin console.</summary>
public sealed record PaymentMethodCatalogDto(
    int Id,
    string Code,
    string Name,
    int SortOrder,
    PaymentCategory Category,
    string SatPaymentFormCode,
    bool RequiresReference,
    bool RequiresCustomer,
    bool SupportsOverpay,
    bool SupportsPartial,
    string? ProviderKey,
    string? CountryCode,
    string? IconClass,
    bool IsActive,
    bool IsSystem);

/// <summary>Create/update payload for a catalog method (metadata; Code immutable on update).</summary>
public sealed record UpsertPaymentMethodCatalogRequest(
    string Code,
    string Name,
    int SortOrder,
    PaymentCategory Category,
    string SatPaymentFormCode,
    bool RequiresReference,
    bool RequiresCustomer,
    bool SupportsOverpay,
    bool SupportsPartial,
    string? ProviderKey,
    string? CountryCode,
    string? IconClass,
    bool IsActive);

/// <summary>One Plan × Method matrix entry.</summary>
public sealed record PlanPaymentMethodEntryDto(int PlanTypeId, int PaymentMethodId, bool IsEnabled);

/// <summary>A per-business override.</summary>
public sealed record TenantPaymentMethodOverrideDto(
    int Id,
    int BusinessId,
    int PaymentMethodId,
    bool IsEnabled,
    string? CustomLabel,
    string? ProviderConfigJson);

/// <summary>Create payload for an override (no Id).</summary>
public sealed record CreateTenantOverrideRequest(
    int BusinessId,
    int PaymentMethodId,
    bool IsEnabled,
    string? CustomLabel,
    string? ProviderConfigJson);

/// <summary>Update payload for an override.</summary>
public sealed record UpdateTenantOverrideRequest(
    bool IsEnabled,
    string? CustomLabel,
    string? ProviderConfigJson);

/// <summary>Impact preview for flipping a method's flag in a plan.</summary>
public sealed record PaymentPreviewImpactDto(
    int AffectedTenantCount,
    IReadOnlyList<AffectedTenantDto> AffectedTenants);

public sealed record AffectedTenantDto(int Id, string BusinessName, string PlanType);

/// <summary>One payment-matrix audit row.</summary>
public sealed record PaymentMatrixAuditEntryDto(
    int Id,
    DateTime ChangedAt,
    string? ChangedByTokenId,
    string Axis,
    string EntityKey,
    string? BeforeJson,
    string? AfterJson);

/// <summary>Paged payment-matrix audit-log response.</summary>
public sealed record PagedPaymentAuditLogDto(
    int Page,
    int PageSize,
    int TotalRows,
    IReadOnlyList<PaymentMatrixAuditEntryDto> Items);
