using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class StockReceiptItem
{
    public int Id { get; set; }

    public int StockReceiptId { get; set; }

    public int? InventoryItemId { get; set; }

    public int? ProductId { get; set; }

    public decimal Quantity { get; set; }

    public int CostCents { get; set; }

    public int TotalCents { get; set; }

    [MaxLength(200)]
    public string? Notes { get; set; }

    public virtual StockReceipt StockReceipt { get; set; } = null!;

    public virtual InventoryItem? InventoryItem { get; set; }

    public virtual Product? Product { get; set; }
}
