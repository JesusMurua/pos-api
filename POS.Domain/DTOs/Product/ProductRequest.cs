using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.DTOs.Product;

/// <summary>
/// Inbound payload for creating or updating a product. Used by both
/// POST /api/products and PUT /api/products/{id}: the Id is taken from
/// the route, not the body, so a single DTO serves both flows.
/// </summary>
public class ProductRequest
{
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

    [MaxLength(100)]
    public string? Barcode { get; set; }

    public bool IsAvailable { get; set; } = true;

    public bool IsPopular { get; set; }

    public bool TrackStock { get; set; } = false;

    public decimal CurrentStock { get; set; } = 0;

    public decimal LowStockThreshold { get; set; } = 0;

    [MaxLength(10)]
    public string? SatProductCode { get; set; }

    [MaxLength(5)]
    public string? SatUnitCode { get; set; }

    public decimal? TaxRate { get; set; }

    public bool IsTaxIncluded { get; set; } = true;

    public PrintingDestination PrintingDestination { get; set; } = PrintingDestination.Kitchen;

    public List<ProductSizeRequest> Sizes { get; set; } = new();

    public List<ProductExtraRequest> Extras { get; set; } = new();
}

/// <summary>
/// Size option to attach to a product during create/update.
/// </summary>
public class ProductSizeRequest
{
    [Required]
    [MaxLength(50)]
    public string Label { get; set; } = null!;

    public int ExtraPriceCents { get; set; }
}

/// <summary>
/// Extra option to attach to a product during create/update.
/// Remains a flat list until the modifier-group refactor lands.
/// </summary>
public class ProductExtraRequest
{
    [Required]
    [MaxLength(100)]
    public string Label { get; set; } = null!;

    public int PriceCents { get; set; }
}
