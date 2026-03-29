using POS.Domain.Enums;

namespace POS.Domain.Models;

/// <summary>
/// Lightweight projection for table status queries.
/// </summary>
public class TableStatusProjection
{
    public int TableId { get; set; }
    public string TableName { get; set; } = null!;
    public int? ZoneId { get; set; }
    public string ZoneName { get; set; } = null!;
    public string? OrderId { get; set; }
    public int? OrderTotalCents { get; set; }
    public KitchenStatus? OrderKitchenStatus { get; set; }
    public DateTime? OrderCreatedAt { get; set; }
}
