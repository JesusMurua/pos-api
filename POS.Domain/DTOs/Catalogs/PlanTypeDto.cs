namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// Read-only projection of a <c>PlanTypeCatalog</c> row.
/// Returned by <c>GET /api/Catalog/plan-types</c>. Distinct from
/// <c>PlanCatalogDto</c> which carries the Plan × Feature matrix.
/// </summary>
/// <param name="Id">Stable identifier (1=Free, 2=Basic, 3=Pro, 4=Enterprise).</param>
/// <param name="Code">Symbolic code (e.g. <c>Basic</c>).</param>
/// <param name="Name">Spanish public label (e.g. <c>Básico</c>).</param>
/// <param name="SortOrder">Ascending display order in pricing tables.</param>
/// <param name="MonthlyPrice">
/// Public monthly price in <see cref="Currency"/>. Null means the plan is
/// not publicly priced (e.g. Enterprise → contact sales).
/// </param>
/// <param name="Currency">ISO 4217 currency code for <see cref="MonthlyPrice"/>.</param>
public sealed record PlanTypeDto(
    int Id,
    string Code,
    string Name,
    int SortOrder,
    decimal? MonthlyPrice,
    string Currency);
