namespace POS.Domain.Models;

public class TableStatusDto
{
    public int TableId { get; set; }
    public string TableName { get; set; } = null!;
    public int? ZoneId { get; set; }
    public string ZoneName { get; set; } = null!;
    public string DisplayStatus { get; set; } = null!;
    public int? OrderTotalCents { get; set; }
    public string? GuestName { get; set; }
    public string? ReservationTime { get; set; }
    public string? OrderId { get; set; }
}
