using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class DashboardService : IDashboardService
{
    private readonly IUnitOfWork _unitOfWork;

    public DashboardService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// Returns dashboard summary for a specific date.
    /// </summary>
    public async Task<DashboardSummaryDto> GetSummaryAsync(int branchId, DateTime date)
    {
        var orders = (await _unitOfWork.Orders.GetAsync(
            o => o.BranchId == branchId
                && o.CreatedAt.Date == date.Date,
            "Items,Payments")).ToList();

        var completed = orders.Where(o => o.CancelledAt == null).ToList();
        var cancelled = orders.Where(o => o.CancelledAt != null).ToList();

        var allPayments = completed.SelectMany(o => o.Payments).ToList();

        var cashCents = allPayments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.AmountCents);
        var cardCents = allPayments.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.AmountCents);
        var transferCents = allPayments.Where(p => p.Method == PaymentMethod.Transfer).Sum(p => p.AmountCents);
        var otherCents = allPayments.Where(p => p.Method == PaymentMethod.Other).Sum(p => p.AmountCents);

        var totalCents = completed.Sum(o => o.TotalCents);

        var topMethod = allPayments
            .GroupBy(p => p.Method)
            .OrderByDescending(g => g.Sum(p => p.AmountCents))
            .FirstOrDefault()?.Key.ToString() ?? "Cash";

        return new DashboardSummaryDto
        {
            Date = date.Date,
            Sales = new DashboardSales
            {
                TotalCents = totalCents,
                CompletedOrders = completed.Count,
                AverageTicketCents = completed.Count > 0 ? totalCents / completed.Count : 0,
                CashCents = cashCents,
                CardCents = cardCents,
                TransferCents = transferCents,
                OtherCents = otherCents,
                TopPaymentMethod = topMethod
            },
            Orders = new DashboardCancellations
            {
                CancelledCount = cancelled.Count,
                CancelledTotalCents = cancelled.Sum(o => o.TotalCents),
                CancellationReasons = cancelled
                    .GroupBy(o => o.CancellationReason ?? "Sin razón")
                    .Select(g => new CancellationReasonDto
                    {
                        Reason = g.Key,
                        Count = g.Count(),
                        TotalCents = g.Sum(o => o.TotalCents)
                    })
                    .OrderByDescending(r => r.Count)
                    .ToList()
            },
            TopProducts = completed
                .Where(o => o.Items != null)
                .SelectMany(o => o.Items!)
                .GroupBy(i => i.ProductName)
                .Select(g => new DashboardTopProduct
                {
                    Name = g.Key,
                    Quantity = g.Sum(i => i.Quantity),
                    TotalCents = g.Sum(i => i.Quantity * i.UnitPriceCents)
                })
                .OrderByDescending(p => p.Quantity)
                .Take(5)
                .ToList(),
            RecentOrders = orders
                .OrderByDescending(o => o.CreatedAt)
                .Take(20)
                .Select(o => new DashboardRecentOrder
                {
                    OrderNumber = o.OrderNumber,
                    ItemCount = o.Items?.Sum(i => i.Quantity) ?? 0,
                    TotalCents = o.TotalCents,
                    KitchenStatus = o.KitchenStatus.ToString(),
                    CancelledAt = o.CancelledAt,
                    CancellationReason = o.CancellationReason,
                    CreatedAt = o.CreatedAt,
                    Payments = o.Payments.Select(p => new DashboardPayment
                    {
                        Method = p.Method.ToString(),
                        AmountCents = p.AmountCents
                    }).ToList()
                })
                .ToList()
        };
    }
}
