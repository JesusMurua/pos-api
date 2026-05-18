using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class SyncOrderRequest
{
    [Required]
    [MaxLength(36)]
    public string Id { get; set; } = null!;

    public int BranchId { get; set; }

    public int OrderNumber { get; set; }

    public int TotalCents { get; set; }

    public DateTime CreatedAt { get; set; }

    public int SubtotalCents { get; set; }

    public int OrderDiscountCents { get; set; }

    public int TotalDiscountCents { get; set; }

    public int? OrderPromotionId { get; set; }

    [MaxLength(100)]
    public string? OrderPromotionName { get; set; }

    public bool IsPaid { get; set; } = false;

    public string? KitchenStatus { get; set; }

    public int? TableId { get; set; }

    [MaxLength(50)]
    public string? TableName { get; set; }

    public int? CashRegisterSessionId { get; set; }

    /// <summary>FK to Customer for CRM tracking. Required when payments use StoreCredit or LoyaltyPoints.</summary>
    public int? CustomerId { get; set; }

    public List<SyncOrderItemRequest> Items { get; set; } = new();

    public List<SyncPaymentRequest> Payments { get; set; } = new();
}

public class SyncOrderItemRequest
{
    public int ProductId { get; set; }

    [Required]
    [MaxLength(150)]
    public string ProductName { get; set; } = null!;

    public decimal Quantity { get; set; }

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

    /// <summary>Vertical-specific JSON payload (e.g. <c>{"BeneficiaryCustomerId": 123}</c> for memberships).</summary>
    public string? Metadata { get; set; }

    /// <summary>
    /// Optional FK to <see cref="POS.Domain.Models.Tax"/> declaring the exact tax
    /// rule the client wants applied to this line. Required when the line has no
    /// catalogued <c>ProductId</c> (custom keypad items) and useful when the
    /// frontend wants to freeze a specific rate against rate-change races.
    /// When supplied, the backend bypasses the resolver chain and uses this Tax
    /// directly. The id is validated against the Tax catalog — invalid ids fail
    /// the entire sync batch.
    /// </summary>
    public int? OverrideTaxId { get; set; }

    /// <summary>
    /// Optional client-declared tax-inclusion flag. Only honored for custom
    /// items (no catalogued <c>ProductId</c>). When a Product exists, the
    /// server uses <see cref="POS.Domain.Models.Product.IsTaxIncluded"/> and
    /// silently ignores this value (Server-Wins trust policy).
    /// </summary>
    public bool? IsTaxIncluded { get; set; }
}

public class SyncPaymentRequest
{
    public string Method { get; set; } = null!;
    public int AmountCents { get; set; }

    [MaxLength(50)]
    public string? Reference { get; set; }

    /// <summary>External provider name: "Clip", "MercadoPago", or null for manual payments.</summary>
    [MaxLength(30)]
    public string? PaymentProvider { get; set; }

    /// <summary>Transaction ID from the external provider.</summary>
    [MaxLength(100)]
    public string? ExternalTransactionId { get; set; }

    /// <summary>JSON string with provider-specific data.</summary>
    public string? PaymentMetadata { get; set; }

    /// <summary>Internal tracking ID for the terminal operation.</summary>
    [MaxLength(100)]
    public string? OperationId { get; set; }

    /// <summary>Payment lifecycle status: "completed", "pending", "failed", "refunded". Required.</summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = null!;
}
