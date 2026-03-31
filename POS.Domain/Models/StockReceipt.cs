using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class StockReceipt
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    public int? SupplierId { get; set; }

    public int ReceivedByUserId { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public int TotalCents { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Branch Branch { get; set; } = null!;

    public virtual Supplier? Supplier { get; set; }

    public virtual User ReceivedBy { get; set; } = null!;

    public virtual ICollection<StockReceiptItem> Items { get; set; } = [];
}
