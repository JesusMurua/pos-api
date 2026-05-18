using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;
using POS.Domain.Models.Metadata;

namespace POS.Domain.Models;

public partial class OrderItem
{
    public int Id { get; set; }

    [Required]
    [MaxLength(36)]
    public string OrderId { get; set; } = null!;

    public int ProductId { get; set; }

    [Required]
    [MaxLength(150)]
    public string ProductName { get; set; } = null!;

    public decimal Quantity { get; set; }

    /// <summary>
    /// Universal product classification frozen at sale time from
    /// <see cref="POS.Domain.Models.Product.Type"/>. Drives line-item rendering
    /// (kg vs units), report categorization, and KDS/print formatting without
    /// requiring a navigation to <see cref="Product"/>. Webhook-ingested items
    /// (UberEats/Rappi/etc.) default to <see cref="ProductType.Standard"/>
    /// because external platforms send no <c>ProductId</c> reference.
    /// </summary>
    public ProductType ProductType { get; set; } = ProductType.Standard;

    public int UnitPriceCents { get; set; }

    [MaxLength(50)]
    public string? SizeName { get; set; }

    public string? ExtrasJson { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    public int DiscountCents { get; set; }

    public int? PromotionId { get; set; }

    [MaxLength(100)]
    public string? PromotionName { get; set; }

    #region Fiscal / SAT Fields (frozen at time of sale)

    /// <summary>SAT product/service code frozen from Product at order creation. E.g., "90101500".</summary>
    [MaxLength(10)]
    public string? SatProductCode { get; set; }

    /// <summary>SAT unit code frozen from Product at order creation. E.g., "H87" = Pieza.</summary>
    [MaxLength(5)]
    public string? SatUnitCode { get; set; }

    /// <summary>
    /// Whether <see cref="UnitPriceCents"/> is gross (tax included) or net.
    /// Frozen at sale time so the tax math survives any subsequent change to
    /// <see cref="POS.Domain.Models.Product.IsTaxIncluded"/>. Drives the
    /// <c>tax_included</c> field sent to Facturapi.
    /// </summary>
    public bool IsTaxIncluded { get; set; } = true;

    #endregion

    /// <summary>
    /// Vertical-specific extensibility payload at the line level, persisted as
    /// PostgreSQL <c>jsonb</c> via EF Core 9 owned-type JSON mapping. Used for
    /// item-scoped data that does not belong on the global order — most notably
    /// <see cref="OrderItemMetadata.BeneficiaryCustomerId"/> for memberships
    /// purchased on behalf of another customer.
    /// </summary>
    public OrderItemMetadata? Metadata { get; set; }

    /// <summary>
    /// Dynamic tenant-specific data. CRITICAL: Lifecycle is managed by EF.
    /// Access RootElement for reads, but CLONE/COPY values if the entity will
    /// be detached/disposed to avoid ObjectDisposedException.
    /// </summary>
    public System.Text.Json.JsonDocument? ExtensionData { get; set; }

    public virtual Order? Order { get; set; }

    public virtual Product? Product { get; set; }

    public virtual ICollection<OrderItemTax> AppliedTaxes { get; set; } = new List<OrderItemTax>();
}
