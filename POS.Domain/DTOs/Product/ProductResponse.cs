using POS.Domain.Enums;
using POS.Domain.Models.Metadata;

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

    /// <summary>
    /// True when at least one <c>OrderItem</c> references this product, i.e. it
    /// has sales history and therefore cannot be hard-deleted (the delete is
    /// blocked by the <c>OnDelete(Restrict)</c> foreign key). The front office
    /// uses this to disable the "Delete" action up front instead of surfacing a
    /// 409 after the click. Stays true even when the product is deactivated, so
    /// the fiscal lock is preserved.
    /// </summary>
    public bool HasOrders { get; set; }

    public bool IsPopular { get; set; }

    public bool TrackStock { get; set; }

    public decimal CurrentStock { get; set; }

    public decimal LowStockThreshold { get; set; }

    /// <summary>
    /// Universal classification mirroring <see cref="POS.Domain.Models.Product.Type"/>.
    /// Serialized as string on the wire via the global JsonStringEnumConverter.
    /// </summary>
    public ProductType Type { get; set; }

    public string? SatProductCode { get; set; }

    public string? SatUnitCode { get; set; }

    public bool IsTaxIncluded { get; set; }

    /// <summary>
    /// Resolved tax rate for this product at read time. Always populated by
    /// <see cref="POS.Services.IService.ITaxResolverService"/> so the frontend
    /// never needs to hardcode a fallback. <c>0</c> means tax-exempt.
    /// </summary>
    public decimal EffectiveTaxRate { get; set; }

    /// <summary>
    /// Mirrors <see cref="POS.Domain.Models.Product.IsTaxIncluded"/> verbatim,
    /// regardless of where <see cref="EffectiveTaxRate"/> was sourced from.
    /// </summary>
    public bool EffectiveIsTaxIncluded { get; set; }

    public PrintingDestination PrintingDestination { get; set; }

    /// <summary>
    /// Strongly-typed vertical-specific payload mirroring
    /// <see cref="POS.Domain.Models.Product.Metadata"/>. Frontend hydrates
    /// niche fields (e.g. <see cref="ProductMetadata.MembershipDurationDays"/>)
    /// directly without parsing raw JSON.
    /// </summary>
    public ProductMetadata? Metadata { get; set; }

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
