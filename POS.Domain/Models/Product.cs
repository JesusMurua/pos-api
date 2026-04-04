using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

public partial class Product
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

    #endregion

    #region Printing

    /// <summary>
    /// Determines which physical area receives the ticket when this product appears in an order.
    /// Defaults to <see cref="PrintingDestination.Kitchen"/> so all existing products retain
    /// their current behavior without requiring a data migration.
    /// </summary>
    public PrintingDestination PrintingDestination { get; set; } = PrintingDestination.Kitchen;

    #endregion

    public virtual Category? Category { get; set; }

    public virtual Branch? Branch { get; set; }

    public virtual ICollection<ProductSize>? Sizes { get; set; }

    public virtual ICollection<ProductExtra>? Extras { get; set; }

    public virtual ICollection<ProductImage>? Images { get; set; }
}
