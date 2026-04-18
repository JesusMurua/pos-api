namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Declares which features are applicable to each <see cref="MacroCategory"/>.
/// A row's presence means "the UI should render this feature for this macro".
/// Absence means the feature is hidden entirely regardless of plan.
/// </summary>
public class BusinessTypeFeature
{
    public int MacroCategoryId { get; set; }

    public int FeatureId { get; set; }

    /// <summary>
    /// Optional per-macro override for quantitative limits.
    /// Only honored when the plan's DefaultLimit is non-null (restrictive).
    /// Lets us express "Free Retail = 500 products, Free everything-else = 50".
    /// </summary>
    public int? Limit { get; set; }

    public MacroCategory? MacroCategory { get; set; }

    public FeatureCatalog? Feature { get; set; }
}
