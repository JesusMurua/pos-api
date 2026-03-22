using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models;

public class CashRegisterSession
{
    public int Id { get; set; }

    public int BranchId { get; set; }

    [Required]
    [MaxLength(100)]
    public string OpenedBy { get; set; } = null!;

    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    public int InitialAmountCents { get; set; }

    [MaxLength(100)]
    public string? ClosedBy { get; set; }

    public DateTime? ClosedAt { get; set; }

    public int? CountedAmountCents { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "open";

    public virtual Branch? Branch { get; set; }

    public virtual ICollection<CashMovement>? Movements { get; set; }
}
