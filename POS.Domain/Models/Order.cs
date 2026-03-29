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

    public DateTime? UpdatedAt { get; set; }

    public int? TableId { get; set; }

    [MaxLength(50)]
    public string? TableName { get; set; }

    public virtual Branch? Branch { get; set; }

    public virtual User? User { get; set; }

    public virtual RestaurantTable? Table { get; set; }

    public virtual ICollection<OrderItem>? Items { get; set; }

    public virtual ICollection<OrderPayment> Payments { get; set; } = new List<OrderPayment>();
}
