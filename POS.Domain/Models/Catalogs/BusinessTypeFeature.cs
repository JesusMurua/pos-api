namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Declares which features are applicable to each business type (giro).
/// A row's presence means "the UI should render this feature for this giro".
/// Absence means the feature is hidden entirely regardless of plan.
/// </summary>
public class BusinessTypeFeature
{
    public int BusinessTypeId { get; set; }

    public int FeatureId { get; set; }

    /// <summary>
    /// Optional per-giro override for quantitative limits.
    /// Only honored when the plan's DefaultLimit is non-null (restrictive).
    /// Lets us express "Free Retail = 500 products, Free everything-else = 50".
    /// </summary>
    public int? Limit { get; set; }

    public BusinessTypeCatalog? BusinessTypeCatalog { get; set; }

    public FeatureCatalog? Feature { get; set; }
}
