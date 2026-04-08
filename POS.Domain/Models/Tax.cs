using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class Tax
{
    public int Id { get; set; }

    [Required]
    [MaxLength(2)]
    public string CountryCode { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    public decimal Rate { get; set; }

    /// <summary>Tax authority code (e.g., "002" for IVA in SAT catalog).</summary>
    [MaxLength(20)]
    public string? Code { get; set; }

    /// <summary>Whether this tax is the default for new products in its country.</summary>
    public bool IsDefault { get; set; }

    public virtual ICollection<ProductTax> ProductTaxes { get; set; } = new List<ProductTax>();
}
