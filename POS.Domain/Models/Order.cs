using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

public partial class Order
{
    [Required]
    [MaxLength(36)]
    public string Id { get; set; } = null!;

    public int BranchId { get; set; }

    public int? UserId { get; set; }

    public int OrderNumber { get; set; }

    public int TotalCents { get; set; }

    public int PaidCents { get; set; }

    public int ChangeCents { get; set; }

    public OrderSyncStatus SyncStatus { get; set; } = OrderSyncStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? SyncedAt { get; set; }

    public int SubtotalCents { get; set; }

    public int OrderDiscountCents { get; set; }

    public int TotalDiscountCents { get; set; }

    public int? OrderPromotionId { get; set; }

    [MaxLength(100)]
    public string? OrderPromotionName { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    public DateTime? CancelledAt { get; set; }

    [MaxLength(100)]
    public string? CancelledBy { get; set; }

    public bool IsPaid { get; set; } = false;

    public KitchenStatus KitchenStatus { get; set; } = KitchenStatus.Pending;

    [MaxLength(20)]
    public string? FolioNumber { get; set; }

    /// <summary>Origin of the order (direct POS or delivery platform).</summary>
    public OrderSource OrderSource { get; set; } = OrderSource.Direct;

    /// <summary>External order ID from the delivery platform (e.g. UE-4821).</summary>
    [MaxLength(50)]
    public string? ExternalOrderId { get; set; }

    /// <summary>Delivery lifecycle status. Null when OrderSource is Direct.</summary>
    public DeliveryStatus? DeliveryStatus { get; set; }

    /// <summary>Name or phone used by the customer on the delivery platform.</summary>
    [MaxLength(100)]
    public string? DeliveryCustomerName { get; set; }

    /// <summary>Estimated pickup time provided by the platform (UTC).</summary>
    public DateTime? EstimatedPickupAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? TableId { get; set; }

    [MaxLength(50)]
    public string? TableName { get; set; }

    public int? CashRegisterSessionId { get; set; }

    /// <summary>FK to Customer for CRM tracking. Null for anonymous sales.</summary>
    public int? CustomerId { get; set; }

    #region Invoicing Fields

    /// <summary>CFDI invoice status for this order.</summary>
    public InvoiceStatus InvoiceStatus { get; set; } = InvoiceStatus.None;

    /// <summary>Facturapi invoice ID linked to this order.</summary>
    [MaxLength(50)]
    public string? FacturapiId { get; set; }

    /// <summary>URL to download the invoice PDF/XML.</summary>
    [MaxLength(500)]
    public string? InvoiceUrl { get; set; }

    /// <summary>Timestamp when the CFDI was issued (timbrado).</summary>
    public DateTime? InvoicedAt { get; set; }

    /// <summary>FK to FiscalCustomer who requested the invoice. Null for global invoices.</summary>
    public int? FiscalCustomerId { get; set; }

    #endregion

    public virtual Branch? Branch { get; set; }

    public virtual User? User { get; set; }

    public virtual RestaurantTable? Table { get; set; }

    public virtual CashRegisterSession? CashRegisterSession { get; set; }

    public virtual FiscalCustomer? FiscalCustomer { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual ICollection<OrderItem>? Items { get; set; }

    public virtual ICollection<OrderPayment> Payments { get; set; } = new List<OrderPayment>();
}
