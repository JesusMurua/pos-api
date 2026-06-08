namespace POS.Domain.Models;

public class DashboardSummaryDto
{
    public DateTime Date { get; set; }
    public DashboardSales Sales { get; set; } = new();
    public DashboardCancellations Orders { get; set; } = new();
    public List<DashboardTopProduct> TopProducts { get; set; } = new();
    public List<DashboardRecentOrder> RecentOrders { get; set; } = new();
}

public class DashboardSales
{
    public int TotalCents { get; set; }
    public int CompletedOrders { get; set; }
    public int AverageTicketCents { get; set; }
    public int CashCents { get; set; }
    public int CardCents { get; set; }

    /// <summary>
    /// Deprecated: transfers now roll into <see cref="DigitalCents"/>. Kept
    /// populated (= DigitalCents) as a non-breaking alias until the FE migrates.
    /// </summary>
    public int TransferCents { get; set; }

    /// <summary>Bank transfers, wallets and QR (digital category).</summary>
    public int DigitalCents { get; set; }

    /// <summary>Customer store credit consumed.</summary>
    public int CreditCents { get; set; }

    /// <summary>Loyalty points redeemed as currency.</summary>
    public int PointsCents { get; set; }

    /// <summary>Vouchers / gift codes.</summary>
    public int VoucherCents { get; set; }

    public int OtherCents { get; set; }
    public string TopPaymentMethod { get; set; } = "Cash";
}

public class DashboardCancellations
{
    public int CancelledCount { get; set; }
    public int CancelledTotalCents { get; set; }
    public List<CancellationReasonDto> CancellationReasons { get; set; } = new();
}

public class CancellationReasonDto
{
    public string Reason { get; set; } = null!;
    public int Count { get; set; }
    public int TotalCents { get; set; }
}

public class DashboardTopProduct
{
    public string Name { get; set; } = null!;
    public decimal Quantity { get; set; }
    public int TotalCents { get; set; }
}

public class DashboardRecentOrder
{
    public int OrderNumber { get; set; }
    public int ItemCount { get; set; }
    public int TotalCents { get; set; }
    public string KitchenStatus { get; set; } = null!;
    public DateTime? CancelledAt { get; set; }
    public string? CancellationReason { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<DashboardPayment> Payments { get; set; } = new();
}

public class DashboardPayment
{
    public string Method { get; set; } = null!;
    public int AmountCents { get; set; }
}

/// <summary>
/// SQL-level projection row for cancellation metrics grouped by reason.
/// </summary>
public class CancellationReasonRow
{
    public string Reason { get; set; } = null!;
    public int Count { get; set; }
    public int TotalCents { get; set; }
}
