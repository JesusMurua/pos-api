using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class DiscountPreset
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = null!;

    [Required]
    [MaxLength(20)]
    public string Type { get; set; } = null!;

    public int Value { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Branch? Branch { get; set; }
}
