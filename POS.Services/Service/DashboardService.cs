using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Helpers;
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
    /// Returns dashboard summary for a specific local calendar day.
    /// Uses SQL-level aggregation via repository projections — no .Include() or entity tracking.
    /// </summary>
    public async Task<DashboardSummaryDto> GetSummaryAsync(int branchId, DateOnly localDate)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId)
            ?? throw new NotFoundException($"Branch with id {branchId} not found");

        var (startUtc, endUtc) = TimeZoneHelper.GetUtcRangeForLocalDate(localDate, branch.TimeZoneId);

        var dailyMetrics = await _unitOfWork.Orders.GetDailyMetricsAsync(branchId, startUtc, endUtc);
        var paymentTotals = await _unitOfWork.Orders.GetPaymentTotalsAsync(branchId, startUtc, endUtc);
        var topProducts = await _unitOfWork.Orders.GetTopProductsAsync(branchId, startUtc, endUtc, top: 5);
        var cancellationReasons = await _unitOfWork.Orders.GetCancellationsByReasonAsync(branchId, startUtc, endUtc);
        var recentOrders = await _unitOfWork.Orders.GetRecentOrdersAsync(branchId, startUtc, endUtc);

        var completedMetrics = dailyMetrics.Where(m => !m.IsCancelled).ToList();
        var cancelledMetrics = dailyMetrics.Where(m => m.IsCancelled).ToList();

        var completedCount = completedMetrics.Sum(m => m.OrderCount);
        var totalCents = completedMetrics.Sum(m => m.TotalCents);

        var cashCents = paymentTotals
            .Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.TotalCents);
        var cardCents = paymentTotals
            .Where(p => p.Method == PaymentMethod.Card).Sum(p => p.TotalCents);
        var transferCents = paymentTotals
            .Where(p => p.Method == PaymentMethod.Transfer).Sum(p => p.TotalCents);
        var otherCents = paymentTotals
            .Where(p => p.Method != PaymentMethod.Cash
                     && p.Method != PaymentMethod.Card
                     && p.Method != PaymentMethod.Transfer)
            .Sum(p => p.TotalCents);

        var topMethod = paymentTotals
            .OrderByDescending(p => p.TotalCents)
            .FirstOrDefault()?.Method.ToString() ?? "Cash";

        return new DashboardSummaryDto
        {
            Date = localDate.ToDateTime(TimeOnly.MinValue),
            Sales = new DashboardSales
            {
                TotalCents = totalCents,
                CompletedOrders = completedCount,
                AverageTicketCents = completedCount > 0 ? totalCents / completedCount : 0,
                CashCents = cashCents,
                CardCents = cardCents,
                TransferCents = transferCents,
                OtherCents = otherCents,
                TopPaymentMethod = topMethod
            },
            Orders = new DashboardCancellations
            {
                CancelledCount = cancelledMetrics.Sum(m => m.OrderCount),
                CancelledTotalCents = cancelledMetrics.Sum(m => m.TotalCents),
                CancellationReasons = cancellationReasons.Select(r => new CancellationReasonDto
                {
                    Reason = r.Reason,
                    Count = r.Count,
                    TotalCents = r.TotalCents
                }).ToList()
            },
            TopProducts = topProducts.Select(p => new DashboardTopProduct
            {
                Name = p.Name,
                Quantity = p.Quantity,
                TotalCents = p.TotalCents
            }).ToList(),
            RecentOrders = recentOrders
        };
    }
}
