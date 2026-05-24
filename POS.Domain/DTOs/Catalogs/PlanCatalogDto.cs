namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// Read-only projection of a single subscription plan together with the
/// list of features it enables. Returned by <c>GET /api/Catalog/plans</c>;
/// single source of truth for the Frontend's plan-comparison UI.
/// </summary>
/// <param name="Id">Stable plan identifier.</param>
/// <param name="Code">Symbolic plan code (e.g. <c>Basic</c>).</param>
/// <param name="Name">Spanish public label (e.g. <c>Básico</c>).</param>
/// <param name="SortOrder">Ascending display order in pricing tables.</param>
/// <param name="MonthlyPrice">
/// Public monthly price in <see cref="Currency"/>. Null means the plan is
/// not publicly priced (e.g. Enterprise → contact sales).
/// </param>
/// <param name="Currency">ISO 4217 currency code for <see cref="MonthlyPrice"/>.</param>
/// <param name="Features">Enabled features inside this plan, sorted by <c>Code</c>.</param>
public sealed record PlanCatalogDto(
    int Id,
    string Code,
    string Name,
    int SortOrder,
    decimal? MonthlyPrice,
    string Currency,
    IReadOnlyList<PlanCatalogFeatureDto> Features);
