using System.ComponentModel.DataAnnotations;

using POS.Domain.Interfaces;

namespace POS.Domain.Models;

public class Device : IBranchScoped
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(100)]
    public string DeviceUuid { get; set; } = null!;

    [Required]
    [MaxLength(50)]
    public string Mode { get; set; } = null!;

    [MaxLength(100)]
    public string? Name { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime? LastSeenAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Branch? Branch { get; set; }
}
