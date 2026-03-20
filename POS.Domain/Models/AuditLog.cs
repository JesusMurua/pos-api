using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class AuditLog
{
    public int Id { get; set; }

    public int? BranchId { get; set; }

    public int? UserId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Action { get; set; } = null!;

    [Required]
    [MaxLength(100)]
    public string EntityType { get; set; } = null!;

    [Required]
    [MaxLength(36)]
    public string EntityId { get; set; } = null!;

    public string? OldValues { get; set; }

    public string? NewValues { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
