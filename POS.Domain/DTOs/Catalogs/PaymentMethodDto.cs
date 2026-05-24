namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// Read-only projection of a <c>PaymentMethodCatalog</c> row.
/// Returned by <c>GET /api/Catalog/payment-methods</c>.
/// </summary>
/// <param name="Id">Stable identifier.</param>
/// <param name="Code">Symbolic code (e.g. <c>Cash</c>, <c>Card</c>, <c>Transfer</c>).</param>
/// <param name="Name">Spanish public label.</param>
/// <param name="SortOrder">Ascending display order in payment selectors.</param>
public sealed record PaymentMethodDto(
    int Id,
    string Code,
    string Name,
    int SortOrder);
