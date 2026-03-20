using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

public partial class User
{
    public int Id { get; set; }

    public int BusinessId { get; set; }

    [Required]
    [MaxLength(150)]
    public string Email { get; set; } = null!;

    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = null!;

    public UserRole Role { get; set; } = UserRole.Cashier;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Business? Business { get; set; }

    public virtual ICollection<Order>? Orders { get; set; }
}
