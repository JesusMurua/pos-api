namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// Read-only projection of a <c>DisplayStatusCatalog</c> row.
/// Returned by <c>GET /api/Catalog/display-statuses</c>. Same shape as
/// <see cref="KitchenStatusDto"/> but kept distinct so the two catalogs
/// can evolve independently.
/// </summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Code">Symbolic code (e.g. <c>free</c>, <c>in_kitchen</c>, <c>ready</c>).</param>
/// <param name="Name">Spanish public label.</param>
/// <param name="Color">Optional hex color (<c>#RRGGBB</c>) for table-map chips.</param>
/// <param name="SortOrder">Ascending display order.</param>
public sealed record DisplayStatusDto(
    int Id,
    string Code,
    string Name,
    string? Color,
    int SortOrder);
