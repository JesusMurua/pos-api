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

    /// <summary>
    /// Hierarchical modifier groups attached to this product. Each group
    /// carries its own selection rules (min/max/required) and owns a list
    /// of <see cref="ProductExtraResponse"/> items.
    /// </summary>
    public List<ProductModifierGroupResponse> ModifierGroups { get; set; } = new();

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
/// Modifier group owned by a product. Groups are the unit of selection
/// logic — "pick 1 protein", "up to 3 sauces" — and wrap a list of
/// individual extras.
/// </summary>
public class ProductModifierGroupResponse
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public int SortOrder { get; set; }

    public bool IsRequired { get; set; }

    public int MinSelectable { get; set; }

    public int MaxSelectable { get; set; }

    public List<ProductExtraResponse> Extras { get; set; } = new();
}

/// <summary>
/// Single selectable option inside a <see cref="ProductModifierGroupResponse"/>.
/// </summary>
public class ProductExtraResponse
{
    public int Id { get; set; }

    public string Label { get; set; } = null!;

    public int PriceCents { get; set; }

    public int SortOrder { get; set; }
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
