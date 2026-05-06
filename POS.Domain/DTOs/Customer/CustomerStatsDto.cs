namespace POS.Domain.DTOs.Customer;

/// <summary>
/// Aggregated customer stats served by <c>GET /api/customers/{id}/stats</c>.
/// Computed via a single DB-level aggregation (SUM, COUNT, MAX) over paid,
/// non-cancelled orders; null SUM/MAX results for customers without orders are
/// coalesced to zero / null at the projection layer.
/// </summary>
public class CustomerStatsDto
{
    public int TotalSpentCents { get; set; }

    public int OrderCount { get; set; }

    public DateTime? LastOrderAt { get; set; }
}
