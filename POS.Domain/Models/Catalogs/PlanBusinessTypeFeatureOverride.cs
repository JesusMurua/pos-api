namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Per (Plan × MacroCategory × Feature) override that takes precedence over the 2D resolution
/// (PlanFeatureMatrix + BusinessTypeFeature). Used to express exceptions like
/// "Quick Service Basic enables RealtimeKds even though the global plan matrix restricts
/// it to Pro+". When present, <see cref="IsEnabled"/> is the final word for the given tuple.
/// </summary>
public class PlanBusinessTypeFeatureOverride
{
    public int PlanTypeId { get; set; }

    public int MacroCategoryId { get; set; }

    public int FeatureId { get; set; }

    /// <summary>Final enablement flag for the specific (plan, macro, feature) tuple.</summary>
    public bool IsEnabled { get; set; }

    public PlanTypeCatalog? PlanTypeCatalog { get; set; }

    public MacroCategory? MacroCategory { get; set; }

    public FeatureCatalog? Feature { get; set; }
}
