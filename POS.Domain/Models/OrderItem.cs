using System.ComponentModel.DataAnnotations;

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

    public int Quantity { get; set; }

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

    /// <summary>IVA tax rate frozen from Product at order creation. 0.16, 0.08, or 0. Null = default 16%.</summary>
    public decimal? TaxRatePercent { get; set; }

    /// <summary>Calculated tax amount in cents: floor(UnitPriceCents * Quantity * TaxRate / (1 + TaxRate)).</summary>
    public int TaxAmountCents { get; set; }

    #endregion

    public virtual Order? Order { get; set; }

    public virtual Product? Product { get; set; }
}
