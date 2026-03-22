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

    public PaymentMethod PaymentMethod { get; set; }

    public int? TenderedCents { get; set; }

    public int? ChangeCents { get; set; }

    public PaymentProvider? PaymentProvider { get; set; }

    [MaxLength(200)]
    public string? ExternalReference { get; set; }

    public OrderSyncStatus SyncStatus { get; set; } = OrderSyncStatus.Pending;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? SyncedAt { get; set; }

    public int? SubtotalCents { get; set; }

    public int? DiscountCents { get; set; }

    [MaxLength(100)]
    public string? DiscountLabel { get; set; }

    [MaxLength(500)]
    public string? DiscountReason { get; set; }

    [MaxLength(500)]
    public string? CancellationReason { get; set; }

    public DateTime? CancelledAt { get; set; }

    [MaxLength(100)]
    public string? CancelledBy { get; set; }

    public virtual Branch? Branch { get; set; }

    public virtual User? User { get; set; }

    public virtual ICollection<OrderItem>? Items { get; set; }
}
