namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// Read-only projection of an <c>AccessMethodCatalog</c> row.
/// Returned by <c>GET /api/Catalog/access-methods</c>.
/// </summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Code">Symbolic code for the access method (e.g. QR, biometric, manual).</param>
/// <param name="Name">Spanish public label.</param>
/// <param name="SortOrder">Ascending display order in access-control pickers.</param>
public sealed record AccessMethodDto(
    int Id,
    string Code,
    string Name,
    int SortOrder);
