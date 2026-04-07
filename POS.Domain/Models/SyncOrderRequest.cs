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
