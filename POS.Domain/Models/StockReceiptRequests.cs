using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class CreateStockReceiptRequest
{
    public int? SupplierId { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    [Required]
    [MinLength(1)]
    public List<StockReceiptItemRequest> Items { get; set; } = [];
}

public class StockReceiptItemRequest
{
    public int? InventoryItemId { get; set; }

    public int? ProductId { get; set; }

    [Range(0.001, double.MaxValue)]
    public decimal Quantity { get; set; }

    [Range(0, int.MaxValue)]
    public int CostCents { get; set; }

    [MaxLength(200)]
    public string? Notes { get; set; }
}
