using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public partial class Branch
{
    public int Id { get; set; }

    public int BusinessId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;

    [MaxLength(200)]
    public string? LocationName { get; set; }

    [MaxLength(255)]
    public string? PinHash { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Business? Business { get; set; }

    public virtual ICollection<Category>? Categories { get; set; }

    public virtual ICollection<Order>? Orders { get; set; }
}
