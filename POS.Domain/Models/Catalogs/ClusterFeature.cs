namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Declares which features are applicable to each sub-giro <c>ClusterCode</c>
/// (the vertical grouping under Macro 4 / Services — e.g. <c>fitness</c>,
/// <c>beauty</c>). Adds a third axis on top of Plan × Macro so verticals that
/// share a macro but not a feature surface (gym vs estética, both Services)
/// can be gated apart.
/// <para>
/// Semantics mirror <see cref="BusinessTypeFeature"/>: a row's <b>presence</b>
/// means "this cluster exposes this feature". A feature becomes cluster-gated
/// only when at least one <see cref="ClusterFeature"/> row exists for it;
/// features with no rows keep pure Plan × Macro resolution (backward compatible).
/// </para>
/// </summary>
public class ClusterFeature
{
    /// <summary>
    /// One of the canonical cluster slugs in <see cref="POS.Domain.Helpers.ClusterCodes"/>.
    /// Constrained at the DB level to the same whitelist as
    /// <c>BusinessTypeCatalog.ClusterCode</c>.
    /// </summary>
    public string ClusterCode { get; set; } = null!;

    public int FeatureId { get; set; }

    public FeatureCatalog? Feature { get; set; }
}
