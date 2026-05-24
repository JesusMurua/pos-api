namespace POS.Domain.DTOs.Catalogs;

/// <summary>
/// One enabled feature inside a <see cref="PlanCatalogDto"/>.
/// </summary>
/// <param name="Code">
/// Stable feature identifier. Exactly the <c>POS.Domain.Enums.FeatureKey</c>
/// enum name (e.g. <c>CustomerDatabase</c>) so the Frontend can compare this
/// against the JWT <c>features</c> claim with a plain string match.
/// </param>
/// <param name="Name">Spanish public label for the feature.</param>
/// <param name="Description">Optional short Spanish description.</param>
/// <param name="IsQuantitative">
/// True for features that carry a numeric cap (e.g. MaxProducts).
/// False for boolean feature gates.
/// </param>
/// <param name="ResourceLabel">
/// Optional resource label used when rendering quantitative-feature errors
/// (e.g. <c>productos</c>, <c>usuarios</c>).
/// </param>
/// <param name="DefaultLimit">
/// Numeric cap for quantitative features. Null means unlimited.
/// Always null for boolean features.
/// </param>
/// <param name="ApplicableMacroCategoryIds">
/// Macro categories where this feature is applicable. Empty when the
/// feature is defined in the catalog but no macro currently exposes it.
/// </param>
public sealed record PlanCatalogFeatureDto(
    string Code,
    string Name,
    string? Description,
    bool IsQuantitative,
    string? ResourceLabel,
    int? DefaultLimit,
    IReadOnlyList<int> ApplicableMacroCategoryIds);
