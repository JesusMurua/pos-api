namespace POS.Domain.Models;

/// <summary>
/// Summary metrics for a date range report.
/// </summary>
public class ReportSummary
{
    public DateTime From { get; set; }
    public DateTime To { get; set; }
    public int TotalOrders { get; set; }
    public int CancelledOrders { get; set; }
    public int CompletedOrders { get; set; }
    public int TotalCents { get; set; }
    public int CashCents { get; set; }
    public int CardCents { get; set; }
    public int DiscountCents { get; set; }
    public decimal AverageTicketCents { get; set; }
    public List<DailySummary> DailySummaries { get; set; } = new();
    public List<TopProduct> TopProducts { get; set; } = new();
    public List<OrderReportRow> Orders { get; set; } = new();
}

/// <summary>
/// Sales summary for a single day.
/// </summary>
public class DailySummary
{
    public DateTime Date { get; set; }
    public int OrderCount { get; set; }
    public int TotalCents { get; set; }
    public int CancelledCount { get; set; }
}

/// <summary>
/// Top selling product in the report period.
/// </summary>
public class TopProduct
{
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int TotalCents { get; set; }
}

/// <summary>
/// Single order row for the report table.
/// </summary>
public class OrderReportRow
{
    public int OrderNumber { get; set; }
    public DateTime CreatedAt { get; set; }
    public int TotalCents { get; set; }
    public int? DiscountCents { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? CancellationReason { get; set; }
    public int ItemCount { get; set; }
}
