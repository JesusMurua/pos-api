namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// Read-only projection of a <c>ZoneTypeCatalog</c> row.
/// Returned by <c>GET /api/Catalog/zone-types</c>.
/// </summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Code">Symbolic code (e.g. <c>Salon</c>, <c>BarSeats</c>, <c>Other</c>).</param>
/// <param name="Name">Spanish public label.</param>
/// <param name="SortOrder">Ascending display order in zone pickers.</param>
public sealed record ZoneTypeDto(
    int Id,
    string Code,
    string Name,
    int SortOrder);
