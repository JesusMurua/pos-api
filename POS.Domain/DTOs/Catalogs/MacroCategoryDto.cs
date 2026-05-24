namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// Read-only projection of a <c>MacroCategory</c> catalog row.
/// Surfaced by <c>GET /api/Catalog/macro-categories</c>; consumed by the
/// Frontend Metadata-Driven Forms (BDD-020) to pick the POS experience
/// variant and toggle kitchen / dine-in form fields.
/// </summary>
/// <param name="Id">Stable identifier (1=FoodBeverage, 2=QuickService, 3=Retail, 4=Services).</param>
/// <param name="InternalCode">Kebab-case symbolic code (e.g. <c>food-beverage</c>).</param>
/// <param name="PublicName">Spanish public label.</param>
/// <param name="Description">Short Spanish description.</param>
/// <param name="PosExperience">
/// One of <c>Restaurant</c>, <c>Counter</c>, <c>Retail</c>, <c>Services</c>, <c>Quick</c>.
/// Drives the Frontend POS variant template selection.
/// </param>
/// <param name="HasKitchen">True when the macro implies a kitchen workflow (KDS / commanda).</param>
/// <param name="HasTables">True when the macro implies dine-in tables.</param>
public sealed record MacroCategoryDto(
    int Id,
    string InternalCode,
    string PublicName,
    string? Description,
    string PosExperience,
    bool HasKitchen,
    bool HasTables);
