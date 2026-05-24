namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// Read-only projection of a <c>KitchenStatusCatalog</c> row.
/// Returned by <c>GET /api/Catalog/kitchen-statuses</c>.
/// </summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Code">Symbolic code (e.g. <c>Pending</c>, <c>Ready</c>, <c>Delivered</c>).</param>
/// <param name="Name">Spanish public label.</param>
/// <param name="Color">Optional hex color (<c>#RRGGBB</c>) for KDS display chips.</param>
/// <param name="SortOrder">Ascending display order in status pickers.</param>
public sealed record KitchenStatusDto(
    int Id,
    string Code,
    string Name,
    string? Color,
    int SortOrder);
