namespace POS.Domain.Models;

/// <summary>
/// Flat projection of an orphaned order for the Backoffice reconciliation view.
/// </summary>
public class OrphanedOrderDto
{
    public string Id { get; set; } = null!;
    public int OrderNumber { get; set; }
    public string? FolioNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SyncedAt { get; set; }
    public int TotalCents { get; set; }
    public int PaidCents { get; set; }
    public bool IsPaid { get; set; }
    public string? TableName { get; set; }
    public int? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public int ItemCount { get; set; }
    public List<string> PaymentMethods { get; set; } = new();
    public string? ReconciliationNote { get; set; }
}
