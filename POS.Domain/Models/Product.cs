using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

using POS.Domain.Interfaces;

namespace POS.Domain.Models;

public partial class Product : IBranchScoped
{
    public int Id { get; set; }

    public int CategoryId { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = null!;

    public int PriceCents { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    [MaxLength(1000)]
    public string? Description { get; set; }

    /// <summary>
    /// Optional barcode (EAN-13, QR, etc.). Unique per branch.
    /// </summary>
    [MaxLength(100)]
    public string? Barcode { get; set; }

    public bool IsAvailable { get; set; } = true;

    public bool IsPopular { get; set; }

    public bool TrackStock { get; set; } = false;

    public decimal CurrentStock { get; set; } = 0;

    public decimal LowStockThreshold { get; set; } = 0;

    #region Fiscal / SAT Fields

    /// <summary>SAT product/service code (clave de producto/servicio). E.g., "90101500" for food.</summary>
    [MaxLength(10)]
    public string? SatProductCode { get; set; }

    /// <summary>SAT unit code (clave de unidad). E.g., "H87" = Pieza, "E48" = Servicio.</summary>
    [MaxLength(5)]
    public string? SatUnitCode { get; set; }

    /// <summary>IVA tax rate for this product. 0.16 (16%), 0.08 (border zone), 0 (exempt). Null = default 16%.</summary>
    public decimal? TaxRate { get; set; }

    /// <summary>Whether PriceCents includes tax. Default true (Mexican standard).</summary>
    public bool IsTaxIncluded { get; set; } = true;

    #endregion

    #region Printing

    /// <summary>
    /// Determines which physical area receives the ticket when this product appears in an order.
    /// Defaults to <see cref="PrintingDestination.Kitchen"/> so all existing products retain
    /// their current behavior without requiring a data migration.
    /// </summary>
    public PrintingDestination PrintingDestination { get; set; } = PrintingDestination.Kitchen;

    #endregion

    /// <summary>
    /// Vertical-specific extensibility payload (JSON). Universal fields stay as strict columns;
    /// niche fields per vertical (e.g. Gym memberships: <c>{"MembershipDurationDays": 30}</c>) live here.
    /// </summary>
    public string? Metadata { get; set; }

    public virtual Category? Category { get; set; }

    public virtual Branch? Branch { get; set; }

    public virtual ICollection<ProductSize>? Sizes { get; set; }

    public virtual ICollection<ProductModifierGroup>? ModifierGroups { get; set; }

    public virtual ICollection<ProductImage>? Images { get; set; }

    public virtual ICollection<ProductTax> ProductTaxes { get; set; } = new List<ProductTax>();
}
