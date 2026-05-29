namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// Read-only projection of a <c>BusinessTypeCatalog</c> sub-giro row.
/// Excludes the <c>PrimaryMacroCategory</c> navigation; clients resolve
/// the macro by calling <c>GET /api/Catalog/macro-categories</c>.
/// </summary>
/// <param name="Id">Stable identifier (e.g. 1=Restaurante, 7=Cafetería).</param>
/// <param name="PrimaryMacroCategoryId">FK into <c>macro-categories</c>.</param>
/// <param name="Name">Public Spanish label (e.g. <c>Cafetería</c>).</param>
/// <param name="ClusterCode">
/// Optional UX grouping slug (e.g. <c>beauty</c>, <c>automotive</c>). Populated
/// only for Services (Macro 4) entries; NULL — and omitted from the JSON
/// payload under the global <c>WhenWritingNull</c> policy — for every other
/// macro. See <c>POS.Domain.Helpers.ClusterCodes</c> for the canonical list.
/// </param>
public sealed record BusinessTypeDto(
    int Id,
    int PrimaryMacroCategoryId,
    string Name,
    string? ClusterCode);
