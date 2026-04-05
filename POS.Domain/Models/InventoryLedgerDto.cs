using POS.Domain.Enums;

namespace POS.Domain.Models;

/// <summary>
/// Flat projection of <see cref="InventoryMovement"/> joined with <see cref="InventoryItem"/>
/// for the global ledger endpoint. Avoids full entity tracking and N+1 queries.
/// </summary>
public class InventoryLedgerDto
{
    public int Id { get; set; }
    public int? InventoryItemId { get; set; }
    public string? ItemName { get; set; }
    public InventoryTransactionType TransactionType { get; set; }
    public decimal Quantity { get; set; }
    public decimal StockAfterTransaction { get; set; }
    public string? Reason { get; set; }
    public string? OrderId { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
}
