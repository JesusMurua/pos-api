using System.ComponentModel.DataAnnotations;
using POS.Domain.Helpers;
using POS.Domain.Models.Catalogs;

using POS.Domain.Interfaces;

namespace POS.Domain.Models;

public partial class RestaurantTable : IBranchScoped
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    public int? Capacity { get; set; }

    /// <summary>FK to TableStatusCatalog.Id (1=Available, 2=Occupied, 3=Reserved, 4=Maintenance).</summary>
    public int TableStatusId { get; set; } = TableStatusIds.Available;

    public int? ZoneId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public TableStatusCatalog? TableStatus { get; set; }

    public virtual Branch? Branch { get; set; }

    public virtual Zone? Zone { get; set; }

    public virtual ICollection<Order>? Orders { get; set; }

    public virtual ICollection<Reservation>? Reservations { get; set; }
}
