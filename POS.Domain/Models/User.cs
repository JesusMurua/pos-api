using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

public partial class User
{
    public int Id { get; set; }

    public int BusinessId { get; set; }

    public int? BranchId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(255)]
    public string? PasswordHash { get; set; }

    [MaxLength(255)]
    public string? PinHash { get; set; }

    public UserRole Role { get; set; } = UserRole.Cashier;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Business? Business { get; set; }

    public virtual Branch? Branch { get; set; }

    public virtual ICollection<Order>? Orders { get; set; }

    /// <summary>
    /// Branches this user belongs to (many-to-many via UserBranch).
    /// </summary>
    public virtual ICollection<UserBranch>? UserBranches { get; set; }

    public virtual ICollection<Reservation>? CreatedReservations { get; set; }
}
