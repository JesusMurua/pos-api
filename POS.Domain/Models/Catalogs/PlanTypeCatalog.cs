using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace POS.Domain.Models.Catalogs;

public class PlanTypeCatalog
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Name { get; set; } = null!;

    public int SortOrder { get; set; }

    /// <summary>
    /// Monthly recurring price expressed in <see cref="Currency"/>. Null means
    /// the plan is not publicly priced (e.g. Enterprise → contact sales).
    /// </summary>
    [Column(TypeName = "numeric(10,2)")]
    public decimal? MonthlyPrice { get; set; }

    /// <summary>ISO 4217 currency code (e.g. "MXN", "USD"). Defaults to "MXN".</summary>
    [Required, MaxLength(3)]
    public string Currency { get; set; } = "MXN";
}
