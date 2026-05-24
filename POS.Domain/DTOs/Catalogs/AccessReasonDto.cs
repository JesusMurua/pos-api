namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// Read-only projection of an <c>AccessReasonCatalog</c> row.
/// Returned by <c>GET /api/Catalog/access-reasons</c>.
/// </summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Code">Symbolic code for the access reason.</param>
/// <param name="Name">Spanish public label.</param>
/// <param name="SortOrder">Ascending display order in access-control pickers.</param>
public sealed record AccessReasonDto(
    int Id,
    string Code,
    string Name,
    int SortOrder);
