namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Read model of a <c>SaaSBillingMethod</c> rail (tenant → operator billing rail).
/// Feeds the admin rail-selection dropdown. Read-only — rails are code-seeded.
/// </summary>
public sealed record SaaSBillingMethodDto(
    int Id,
    string Code,
    string Name,
    bool IsAutomatic,
    bool RequiresReference,
    string? ProviderKey,
    string? CountryCode,
    int SortOrder,
    bool IsActive,
    bool IsSystem);

/// <summary>
/// Read model of a <c>PlanAddOn</c> catalog row. Feeds the admin add-on-selection
/// dropdown. <c>billingCycle</c> / <c>linkType</c> are the stable PascalCase enum names.
/// </summary>
public sealed record PlanAddOnDto(
    int Id,
    string Code,
    string Name,
    string? Description,
    string BillingCycle,
    int DefaultPriceCents,
    string Currency,
    string LinkType,
    int? LinkedEntityId,
    string? StripePriceId,
    bool IsActive,
    bool IsSystem);
