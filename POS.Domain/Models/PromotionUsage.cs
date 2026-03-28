using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class PromotionUsage
{
    public int Id { get; set; }

    public int PromotionId { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(36)]
    public string OrderId { get; set; } = null!;

    public DateTime UsedAt { get; set; } = DateTime.UtcNow;

    public virtual Promotion Promotion { get; set; } = null!;
}
