using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

public class Reservation
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    public int? TableId { get; set; }

    [Required]
    [MaxLength(100)]
    public string GuestName { get; set; } = null!;

    [MaxLength(20)]
    public string? GuestPhone { get; set; }

    [Range(1, int.MaxValue)]
    public int PartySize { get; set; }

    public DateOnly ReservationDate { get; set; }

    public TimeOnly ReservationTime { get; set; }

    public int DurationMinutes { get; set; } = 90;

    public ReservationStatus Status { get; set; } = ReservationStatus.Pending;

    [MaxLength(500)]
    public string? Notes { get; set; }

    public int CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public virtual Branch? Branch { get; set; }

    public virtual RestaurantTable? Table { get; set; }

    public virtual User? CreatedByUser { get; set; }
}
