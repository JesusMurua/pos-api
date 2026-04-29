using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models.Catalogs;

/// <summary>
/// Catalog of all gatable features in the system.
/// Each row maps 1:1 to a FeatureKey enum value.
/// </summary>
public class FeatureCatalog
{
    public int Id { get; set; }

    /// <summary>Stable string key matching the FeatureKey enum name.</summary>
    [Required, MaxLength(50)]
    public string Code { get; set; } = null!;

    /// <summary>Enum representation used by services and attributes.</summary>
    public FeatureKey Key { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(300)]
    public string? Description { get; set; }

    /// <summary>
    /// True for features that carry a numeric cap (MaxProducts, MaxUsers, ...).
    /// False for boolean on/off features.
    /// </summary>
    public bool IsQuantitative { get; set; }

    /// <summary>
    /// Resource label used in user-facing plan limit error messages (e.g. "productos", "usuarios").
    /// Only meaningful when IsQuantitative is true.
    /// </summary>
    [MaxLength(50)]
    public string? ResourceLabel { get; set; }

    public int SortOrder { get; set; }

    /// <summary>
    /// Aggregation scope used by the device-licensing enforcement engine.
    /// <c>Global</c> counts devices across the entire business; <c>Branch</c>
    /// counts within a single sucursal. Only consulted for quantitative
    /// features whose Code maps to a hardware <c>DeviceMode</c>.
    /// </summary>
    public EnforcementScope Scope { get; set; } = EnforcementScope.Global;
}
