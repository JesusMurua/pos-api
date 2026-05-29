using System.ComponentModel.DataAnnotations;

namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Payload for <c>POST /api/Admin/catalogs/invalidate</c>. The controller
/// rejects any key not present in its server-side whitelist (the 11 catalog
/// keys served by <c>CatalogController</c>); unknown or misspelled keys
/// produce a 400 with the offending list so the operator can correct and
/// retry without invalidating the partial set.
/// </summary>
/// <param name="CatalogKeys">
/// One or more catalog cache keys to evict, e.g. <c>["BusinessTypes"]</c> or
/// <c>["MacroCategories","BusinessTypes"]</c>. Case-insensitive on input;
/// normalized to canonical PascalCase before being passed to the cache layer.
/// </param>
public sealed record AdminCatalogInvalidateRequest(
    [Required, MinLength(1)] IReadOnlyList<string> CatalogKeys);
