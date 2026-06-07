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
    /// Returns dashboard summary for a local calendar day in the branch's timezone.
    /// When <paramref name="localDate"/> is <c>null</c>, defaults to today in the
    /// branch's timezone — never the server's UTC wall-clock.
    /// Uses SQL-level aggregation via repository projections — no .Include() or entity tracking.
    /// </summary>
    public async Task<DashboardSummaryDto> GetSummaryAsync(int branchId, DateOnly? localDate)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId)
            ?? throw new NotFoundException($"Branch with id {branchId} not found");

        var targetDate = localDate ?? TimeZoneHelper.GetLocalToday(branch.TimeZoneId);

        var (startUtc, endUtc) = TimeZoneHelper.GetUtcRangeForLocalDate(targetDate, branch.TimeZoneId);

        var dailyMetrics = await _unitOfWork.Orders.GetDailyMetricsAsync(branchId, startUtc, endUtc);
        var paymentTotals = await _unitOfWork.Orders.GetPaymentTotalsAsync(branchId, startUtc, endUtc);
        var topProducts = await _unitOfWork.Orders.GetTopProductsAsync(branchId, startUtc, endUtc, top: 5);
        var cancellationReasons = await _unitOfWork.Orders.GetCancellationsByReasonAsync(branchId, startUtc, endUtc);
        var recentOrders = await _unitOfWork.Orders.GetRecentOrdersAsync(branchId, startUtc, endUtc);

        var completedMetrics = dailyMetrics.Where(m => !m.IsCancelled).ToList();
        var cancelledMetrics = dailyMetrics.Where(m => m.IsCancelled).ToList();

        var completedCount = completedMetrics.Sum(m => m.OrderCount);
        var totalCents = completedMetrics.Sum(m => m.TotalCents);

        // Group by payment bucket (Cash already net of change at the repository).
        // Card-backed terminals (Clip, BankTerminal) fold into the Card bucket;
        // MercadoPago/credit/points/other fall to Other — see PaymentMethodBuckets.
        int SumBucket(PaymentBucket bucket) => paymentTotals
            .Where(p => PaymentMethodBuckets.BucketOf(p.Method) == bucket)
            .Sum(p => p.TotalCents);

        var cashCents = SumBucket(PaymentBucket.Cash);
        var cardCents = SumBucket(PaymentBucket.Card);
        var transferCents = SumBucket(PaymentBucket.Transfer);
        var otherCents = SumBucket(PaymentBucket.Other);

        var topMethod = paymentTotals
            .OrderByDescending(p => p.TotalCents)
            .FirstOrDefault()?.Method.ToString() ?? "Cash";

        return new DashboardSummaryDto
        {
            Date = targetDate.ToDateTime(TimeOnly.MinValue),
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
