namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Junction between PlanTypeCatalog and FeatureCatalog.
/// Declares which features are enabled for each plan tier and the default cap for quantitative features.
/// </summary>
public class PlanFeatureMatrix
{
    public int PlanTypeId { get; set; }

    public int FeatureId { get; set; }

    /// <summary>True if the plan includes the feature.</summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Cap for quantitative features. Null means unlimited.
    /// Ignored for boolean features.
    /// </summary>
    public int? DefaultLimit { get; set; }

    public PlanTypeCatalog? PlanTypeCatalog { get; set; }

    public FeatureCatalog? Feature { get; set; }
}
