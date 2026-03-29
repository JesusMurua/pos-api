using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public partial class RestaurantTable
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    public int? Capacity { get; set; }

    /// <summary>
    /// Table occupancy status: available | occupied.
    /// </summary>
    [MaxLength(20)]
    public string Status { get; set; } = "available";

    public int? ZoneId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Branch? Branch { get; set; }

    public virtual Zone? Zone { get; set; }

    public virtual ICollection<Order>? Orders { get; set; }

    public virtual ICollection<Reservation>? Reservations { get; set; }
}
