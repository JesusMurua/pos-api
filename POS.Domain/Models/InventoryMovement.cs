using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

public class InventoryMovement
{
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to <see cref="InventoryItem"/>.
    /// Null when the movement targets a <see cref="Product"/> via the TrackStock direct path.
    /// </summary>
    public int? InventoryItemId { get; set; }

    /// <summary>
    /// Foreign key to <see cref="Product"/>.
    /// Populated only for the TrackStock direct deduction path (no recipe involved).
    /// </summary>
    public int? ProductId { get; set; }

    /// <summary>
    /// Semantic transaction type. Supersedes the legacy <see cref="Type"/> string.
    /// </summary>
    public InventoryTransactionType TransactionType { get; set; } = InventoryTransactionType.ConsumeFromSale;

    /// <summary>FK to InventoryMovementTypeCatalog.Id (1=In, 2=Out, 3=Adjustment).</summary>
    public int InventoryMovementTypeId { get; set; }

    /// <summary>
    /// Absolute quantity involved in this movement. Always positive.
    /// Direction (add/subtract) is determined by <see cref="TransactionType"/>.
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Snapshot of <see cref="InventoryItem.CurrentStock"/> immediately after this
    /// movement was applied. Makes the ledger self-sufficient — historical stock
    /// can be read at any point without replaying all prior movements.
    /// </summary>
    public decimal StockAfterTransaction { get; set; }

    /// <summary>
    /// Free-text note about the movement.
    /// Required when <see cref="TransactionType"/> is <c>Waste</c> or <c>ManualAdjustment</c>.
    /// </summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>
    /// Reference to the order that triggered this movement.
    /// Populated when <see cref="TransactionType"/> is <c>ConsumeFromSale</c>.
    /// </summary>
    [MaxLength(36)]
    public string? OrderId { get; set; }

    /// <summary>
    /// Identity of who originated this movement.
    /// Values: a username/email, "SyncEngine", "System", or "StockReceipt".
    /// </summary>
    [MaxLength(100)]
    public string? CreatedBy { get; set; }

    /// <summary>User ID of the person who registered this movement. Nullable for automated entries.</summary>
    public int? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual InventoryItem? InventoryItem { get; set; }

    public Catalogs.InventoryMovementTypeCatalog? InventoryMovementType { get; set; }
}
