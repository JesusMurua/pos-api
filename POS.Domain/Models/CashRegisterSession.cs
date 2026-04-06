using System.ComponentModel.DataAnnotations;
using POS.Domain.Helpers;

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

    /// <summary>Sum of cash payments from orders during the session.</summary>
    public int? CashSalesCents { get; set; }

    /// <summary>Sum of "in" cash movements.</summary>
    public int? TotalCashInCents { get; set; }

    /// <summary>Sum of "out" cash movements.</summary>
    public int? TotalCashOutCents { get; set; }

    /// <summary>InitialAmountCents + CashSalesCents + TotalCashInCents - TotalCashOutCents.</summary>
    public int? ExpectedAmountCents { get; set; }

    /// <summary>CountedAmountCents - ExpectedAmountCents (positive = surplus, negative = shortage).</summary>
    public int? DifferenceCents { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = CashRegisterStatus.Open;

    /// <summary>Bumped on every mutation to trigger xmin concurrency token update.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int? CashRegisterId { get; set; }

    public virtual Branch? Branch { get; set; }

    public virtual CashRegister? CashRegister { get; set; }

    public virtual ICollection<CashMovement>? Movements { get; set; }

    public virtual ICollection<Order>? Orders { get; set; }
}
