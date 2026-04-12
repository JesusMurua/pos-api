using POS.Domain.Enums;

namespace POS.Domain.DTOs.Product;

/// <summary>
/// Outbound representation of a product returned to API consumers.
/// Decouples the Entity Framework model from the public API contract.
/// </summary>
public class ProductResponse
{
    public int Id { get; set; }

    public int CategoryId { get; set; }

    public int BranchId { get; set; }

    public string Name { get; set; } = null!;

    public int PriceCents { get; set; }

    public string? ImageUrl { get; set; }

    public string? Description { get; set; }

    public string? Barcode { get; set; }

    public bool IsAvailable { get; set; }

    public bool IsPopular { get; set; }

    public bool TrackStock { get; set; }

    public decimal CurrentStock { get; set; }

    public decimal LowStockThreshold { get; set; }

    public string? SatProductCode { get; set; }

    public string? SatUnitCode { get; set; }

    public decimal? TaxRate { get; set; }

    public bool IsTaxIncluded { get; set; }

    public PrintingDestination PrintingDestination { get; set; }

    public List<ProductSizeResponse> Sizes { get; set; } = new();

    public List<ProductExtraResponse> Extras { get; set; } = new();

    public List<ProductImageResponse> Images { get; set; } = new();
}

/// <summary>
/// Flat size option attached to a product (e.g. "Chico", "Grande").
/// </summary>
public class ProductSizeResponse
{
    public int Id { get; set; }

    public string Label { get; set; } = null!;

    public int ExtraPriceCents { get; set; }
}

/// <summary>
/// Flat extra option attached to a product. Will become part of a
/// modifier group in a subsequent phase; currently exposed as a flat list
/// to preserve the existing frontend contract.
/// </summary>
public class ProductExtraResponse
{
    public int Id { get; set; }

    public string Label { get; set; } = null!;

    public int PriceCents { get; set; }
}

/// <summary>
/// Image attached to a product, stored in Supabase Storage.
/// </summary>
public class ProductImageResponse
{
    public int Id { get; set; }

    public string Url { get; set; } = null!;

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; }
}
