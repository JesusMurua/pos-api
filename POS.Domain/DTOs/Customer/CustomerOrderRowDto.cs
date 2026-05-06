namespace POS.Domain.DTOs.Customer;

/// <summary>
/// Lightweight projection of an <see cref="POS.Domain.Models.Order"/> row used
/// by the Admin "Customer Detail → History" panel. Materialized via pure EF
/// projection (no entity hydration, no JSON columns loaded).
/// </summary>
public class CustomerOrderRowDto
{
    public string OrderId { get; set; } = null!;

    public int OrderNumber { get; set; }

    public DateTime CreatedAt { get; set; }

    public int TotalCents { get; set; }

    /// <summary>Count of <see cref="POS.Domain.Models.OrderItem"/> rows; computed via SQL subquery.</summary>
    public int ItemCount { get; set; }

    public int BranchId { get; set; }

    public string BranchName { get; set; } = string.Empty;

    public bool IsPaid { get; set; }

    public string? CancellationReason { get; set; }
}
