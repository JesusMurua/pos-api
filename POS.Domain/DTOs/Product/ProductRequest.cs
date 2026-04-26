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

    /// <summary>
    /// Vertical-specific extensibility payload (JSON). E.g. Gym memberships
    /// send <c>{"MembershipDurationDays": 30}</c>. Stored verbatim.
    /// </summary>
    [MaxLength(1000)]
    public string? Metadata { get; set; }

    public List<ProductSizeRequest> Sizes { get; set; } = new();

    /// <summary>
    /// Hierarchical modifier groups. Each group defines its own
    /// selection rules and owns its extras; the server no longer
    /// accepts a flat extras list.
    /// </summary>
    public List<ProductModifierGroupRequest> ModifierGroups { get; set; } = new();
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
/// Modifier group payload. The backend does not trust the client-provided
/// Id and treats every update as a wholesale replacement of the product's
/// groups, so Id is intentionally absent here.
/// </summary>
public class ProductModifierGroupRequest
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    public int SortOrder { get; set; }

    public bool IsRequired { get; set; }

    public int MinSelectable { get; set; }

    public int MaxSelectable { get; set; }

    public List<ProductExtraRequest> Extras { get; set; } = new();
}

/// <summary>
/// Extra option to attach to a modifier group during create/update.
/// </summary>
public class ProductExtraRequest
{
    [Required]
    [MaxLength(100)]
    public string Label { get; set; } = null!;

    public int PriceCents { get; set; }

    public int SortOrder { get; set; }
}
