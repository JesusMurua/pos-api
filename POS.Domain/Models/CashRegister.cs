using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class CashRegister
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [MaxLength(100)]
    public string? DeviceUuid { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Branch? Branch { get; set; }

    public virtual ICollection<CashRegisterSession>? Sessions { get; set; }
}
