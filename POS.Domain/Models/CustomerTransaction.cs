using System.ComponentModel.DataAnnotations;
using POS.Domain.Enums;

namespace POS.Domain.Models;

/// <summary>
/// Ledger entry for customer credit and loyalty point movements.
/// This is the source of truth — Customer.CreditBalanceCents and PointsBalance
/// are denormalized snapshots that can be recalculated from this ledger.
/// </summary>
public class CustomerTransaction
{
    public int Id { get; set; }

    /// <summary>The customer this transaction belongs to.</summary>
    public int CustomerId { get; set; }

    /// <summary>Branch where the transaction occurred.</summary>
    public int BranchId { get; set; }

    /// <summary>Type of transaction (earn/redeem points, add/use credit, adjustments).</summary>
    public CustomerTransactionType TransactionType { get; set; }

    /// <summary>
    /// Amount in cents for credit transactions.
    /// Positive = credit added / earned. Negative = credit used / redeemed.
    /// Zero for pure points-only transactions.
    /// </summary>
    public int AmountCents { get; set; }

    /// <summary>
    /// Points amount for loyalty transactions.
    /// Positive = points earned. Negative = points redeemed.
    /// Zero for pure credit-only transactions.
    /// </summary>
    public int PointsAmount { get; set; }

    /// <summary>Customer's credit balance snapshot after this transaction.</summary>
    public int BalanceAfterCents { get; set; }

    /// <summary>Customer's points balance snapshot after this transaction.</summary>
    public int PointsBalanceAfter { get; set; }

    /// <summary>FK to the related order, if applicable. Null for manual adjustments.</summary>
    [MaxLength(36)]
    public string? ReferenceOrderId { get; set; }

    /// <summary>Human-readable description of the movement (e.g., "Fiado - Orden #42").</summary>
    [Required]
    [MaxLength(200)]
    public string Description { get; set; } = null!;

    /// <summary>User who created this transaction.</summary>
    [Required]
    [MaxLength(100)]
    public string CreatedBy { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual Customer? Customer { get; set; }

    public virtual Branch? Branch { get; set; }

    public virtual Order? ReferenceOrder { get; set; }
}
