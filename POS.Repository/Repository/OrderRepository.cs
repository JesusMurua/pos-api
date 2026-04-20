using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class OrderRepository : GenericRepository<Order>, IOrderRepository
{
    public OrderRepository(ApplicationDbContext context) : base(context)
    {
    }

    #region Day / Range Queries

    /// <inheritdoc />
    public async Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateTime startUtc, DateTime endUtc)
    {
        EnsureUtcRange(startUtc, endUtc);

        return await _context.Orders
            .Where(o => o.BranchId == branchId
                     && o.CreatedAt >= startUtc
                     && o.CreatedAt < endUtc)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateTime startUtc, DateTime endUtc)
    {
        EnsureUtcRange(startUtc, endUtc);

        return await _context.Orders
            .Where(o => o.BranchId == branchId
                     && o.CreatedAt >= startUtc
                     && o.CreatedAt < endUtc
                     && o.SyncStatusId != SyncStatusIds.Failed)
            .Include(o => o.Items)
            .ToListAsync();
    }

    #endregion

    #region Misc Queries (unchanged semantics)

    public async Task<IEnumerable<Order>> GetPendingSyncAsync()
    {
        return await _context.Orders
            .Where(o => o.SyncStatusId == SyncStatusIds.Pending)
            .Include(o => o.Items)
            .ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetPullOrdersAsync(int branchId, DateTime since)
    {
        return await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                && o.CancellationReason == null
                && (o.UpdatedAt > since || o.CreatedAt > since))
            .Include(o => o.Items)
            .Include(o => o.Payments)
            .OrderByDescending(o => o.UpdatedAt ?? o.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Order>> GetActiveDeliveryOrdersAsync(int branchId)
    {
        return await _context.Orders
            .Where(o => o.BranchId == branchId
                && o.OrderSource != OrderSource.Direct
                && o.DeliveryStatus != DeliveryStatus.PickedUp
                && o.DeliveryStatus != DeliveryStatus.Rejected)
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<Order?> GetByExternalIdAsync(int branchId, string externalOrderId)
    {
        return await _context.Orders
            .FirstOrDefaultAsync(o => o.BranchId == branchId
                && o.ExternalOrderId == externalOrderId);
    }

    #endregion

    #region BDD-006b: High-performance report projections

    /// <inheritdoc />
    public async Task<List<OrderDailyMetricRow>> GetDailyMetricsAsync(int branchId, DateTime startUtc, DateTime endUtc)
    {
        EnsureUtcRange(startUtc, endUtc);

        return await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                     && o.CreatedAt >= startUtc
                     && o.CreatedAt < endUtc)
            .GroupBy(o => new { o.CreatedAt.Date, IsCancelled = o.CancellationReason != null })
            .Select(g => new OrderDailyMetricRow
            {
                Date = g.Key.Date,
                IsCancelled = g.Key.IsCancelled,
                OrderCount = g.Count(),
                TotalCents = g.Sum(o => o.TotalCents),
                DiscountCents = g.Sum(o => o.TotalDiscountCents)
            })
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<PaymentMethodTotalRow>> GetPaymentTotalsAsync(int branchId, DateTime startUtc, DateTime endUtc)
    {
        EnsureUtcRange(startUtc, endUtc);

        return await _context.OrderPayments
            .AsNoTracking()
            .Where(p => p.Order.BranchId == branchId
                     && p.Order.CreatedAt >= startUtc
                     && p.Order.CreatedAt < endUtc
                     && p.Order.CancellationReason == null)
            .GroupBy(p => p.Method)
            .Select(g => new PaymentMethodTotalRow
            {
                Method = g.Key,
                TotalCents = g.Sum(p => p.AmountCents)
            })
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<TopProduct>> GetTopProductsAsync(int branchId, DateTime startUtc, DateTime endUtc, int top = 10)
    {
        EnsureUtcRange(startUtc, endUtc);

        return await _context.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.BranchId == branchId
                     && i.Order.CreatedAt >= startUtc
                     && i.Order.CreatedAt < endUtc
                     && i.Order.CancellationReason == null)
            .GroupBy(i => i.ProductName)
            .Select(g => new TopProduct
            {
                Name = g.Key,
                Quantity = g.Sum(i => i.Quantity),
                TotalCents = g.Sum(i => i.Quantity * i.UnitPriceCents)
            })
            .OrderByDescending(p => p.Quantity)
            .Take(top)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<OrderReportRow>> GetFlatOrderRowsAsync(int branchId, DateTime startUtc, DateTime endUtc)
    {
        EnsureUtcRange(startUtc, endUtc);

        var rows = await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                     && o.CreatedAt >= startUtc
                     && o.CreatedAt < endUtc)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.OrderNumber,
                o.CreatedAt,
                o.TotalCents,
                o.TotalDiscountCents,
                PaymentMethods = o.Payments.Select(p => p.Method),
                o.CancellationReason,
                ItemCount = o.Items!.Count
            })
            .ToListAsync();

        return rows.Select(o => new OrderReportRow
        {
            OrderNumber = o.OrderNumber,
            CreatedAt = o.CreatedAt,
            TotalCents = o.TotalCents,
            TotalDiscountCents = o.TotalDiscountCents,
            PaymentMethod = string.Join(", ", o.PaymentMethods.Select(m => m.ToString()).Distinct()),
            Status = o.CancellationReason != null ? "Cancelada" : "Completada",
            CancellationReason = o.CancellationReason,
            ItemCount = o.ItemCount
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<List<FiscalCsvRow>> GetFlatOrdersForCsvAsync(int branchId, DateTime startUtc, DateTime endUtc)
    {
        EnsureUtcRange(startUtc, endUtc);

        var rows = await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                     && o.CreatedAt >= startUtc
                     && o.CreatedAt < endUtc)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.CreatedAt,
                o.TotalCents,
                PaymentMethods = o.Payments.Select(p => p.Method),
                o.InvoiceStatus
            })
            .ToListAsync();

        return rows.Select(r => new FiscalCsvRow
        {
            Id = r.Id,
            CreatedAt = r.CreatedAt,
            TotalCents = r.TotalCents,
            PaymentMethods = r.PaymentMethods.ToList(),
            InvoiceStatus = r.InvoiceStatus
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<List<SalesPointDto>> GetSalesOverTimeAsync(
        int branchId, DateTime startUtc, DateTime endUtc, string granularity)
    {
        EnsureUtcRange(startUtc, endUtc);

        var baseQuery = _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                     && o.IsPaid
                     && o.CancellationReason == null
                     && o.CreatedAt >= startUtc
                     && o.CreatedAt < endUtc);

        switch (granularity?.ToLowerInvariant())
        {
            case "hour":
                var hourData = await baseQuery
                    .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month, o.CreatedAt.Day, o.CreatedAt.Hour })
                    .Select(g => new
                    {
                        g.Key.Year, g.Key.Month, g.Key.Day, g.Key.Hour,
                        TotalCents = g.Sum(o => o.TotalCents),
                        OrderCount = g.Count()
                    })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day).ThenBy(x => x.Hour)
                    .ToListAsync();

                return hourData.Select(x => new SalesPointDto
                {
                    Date = new DateTime(x.Year, x.Month, x.Day, x.Hour, 0, 0),
                    TotalCents = x.TotalCents,
                    OrderCount = x.OrderCount
                }).ToList();

            case "month":
                var monthData = await baseQuery
                    .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month })
                    .Select(g => new
                    {
                        g.Key.Year, g.Key.Month,
                        TotalCents = g.Sum(o => o.TotalCents),
                        OrderCount = g.Count()
                    })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month)
                    .ToListAsync();

                return monthData.Select(x => new SalesPointDto
                {
                    Date = new DateTime(x.Year, x.Month, 1),
                    TotalCents = x.TotalCents,
                    OrderCount = x.OrderCount
                }).ToList();

            default: // "day"
                var dayData = await baseQuery
                    .GroupBy(o => new { o.CreatedAt.Year, o.CreatedAt.Month, o.CreatedAt.Day })
                    .Select(g => new
                    {
                        g.Key.Year, g.Key.Month, g.Key.Day,
                        TotalCents = g.Sum(o => o.TotalCents),
                        OrderCount = g.Count()
                    })
                    .OrderBy(x => x.Year).ThenBy(x => x.Month).ThenBy(x => x.Day)
                    .ToListAsync();

                return dayData.Select(x => new SalesPointDto
                {
                    Date = new DateTime(x.Year, x.Month, x.Day),
                    TotalCents = x.TotalCents,
                    OrderCount = x.OrderCount
                }).ToList();
        }
    }

    /// <inheritdoc />
    public async Task<List<TopProductDto>> GetTopProductsBIAsync(
        int branchId, DateTime startUtc, DateTime endUtc, int top = 10)
    {
        EnsureUtcRange(startUtc, endUtc);

        return await _context.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.BranchId == branchId
                     && i.Order.IsPaid
                     && i.Order.CancellationReason == null
                     && i.Order.CreatedAt >= startUtc
                     && i.Order.CreatedAt < endUtc)
            .GroupBy(i => i.ProductName)
            .Select(g => new TopProductDto
            {
                ProductName = g.Key,
                QuantitySold = g.Sum(i => i.Quantity),
                TotalRevenueCents = g.Sum(i => i.Quantity * i.UnitPriceCents)
            })
            .OrderByDescending(p => p.QuantitySold)
            .Take(top)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<PaymentMethodSalesDto>> GetSalesByPaymentMethodAsync(
        int branchId, DateTime startUtc, DateTime endUtc)
    {
        EnsureUtcRange(startUtc, endUtc);

        var rawPayments = await _context.OrderPayments
            .AsNoTracking()
            .Where(p => p.Order.BranchId == branchId
                     && p.Order.IsPaid
                     && p.Order.CancellationReason == null
                     && p.Order.CreatedAt >= startUtc
                     && p.Order.CreatedAt < endUtc)
            .GroupBy(p => new { p.Method, p.PaymentProvider })
            .Select(g => new
            {
                Method = g.Key.Method,
                Provider = g.Key.PaymentProvider,
                TotalCents = g.Sum(p => p.AmountCents),
                TransactionCount = g.Count()
            })
            .OrderByDescending(x => x.TotalCents)
            .ToListAsync();

        return rawPayments.Select(r => new PaymentMethodSalesDto
        {
            PaymentMethod = r.Method.ToString(),
            Provider = r.Provider,
            TotalCents = r.TotalCents,
            TransactionCount = r.TransactionCount
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<List<DetailedSalesCsvRow>> GetDetailedSalesCsvRowsAsync(
        int branchId, DateTime startUtc, DateTime endUtc)
    {
        EnsureUtcRange(startUtc, endUtc);

        var rows = await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                     && o.IsPaid
                     && o.CancellationReason == null
                     && o.CreatedAt >= startUtc
                     && o.CreatedAt < endUtc)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.CreatedAt,
                o.TotalCents,
                PaymentMethods = o.Payments.Select(p => p.Method),
                CustomerFirstName = o.Customer != null ? o.Customer.FirstName : string.Empty,
                CustomerLastName = o.Customer != null ? o.Customer.LastName : null,
                o.InvoiceStatus
            })
            .ToListAsync();

        return rows.Select(r => new DetailedSalesCsvRow
        {
            Id = r.Id,
            CreatedAt = r.CreatedAt,
            TotalCents = r.TotalCents,
            PaymentMethods = r.PaymentMethods.ToList(),
            CustomerFirstName = r.CustomerFirstName,
            CustomerLastName = r.CustomerLastName,
            InvoiceStatus = r.InvoiceStatus
        }).ToList();
    }

    #endregion

    #region AUDIT-001 P0: Dashboard projections

    /// <inheritdoc />
    public async Task<List<CancellationReasonRow>> GetCancellationsByReasonAsync(int branchId, DateTime startUtc, DateTime endUtc)
    {
        EnsureUtcRange(startUtc, endUtc);

        return await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                     && o.CreatedAt >= startUtc
                     && o.CreatedAt < endUtc
                     && o.CancellationReason != null)
            .GroupBy(o => o.CancellationReason!)
            .Select(g => new CancellationReasonRow
            {
                Reason = g.Key,
                Count = g.Count(),
                TotalCents = g.Sum(o => o.TotalCents)
            })
            .OrderByDescending(r => r.Count)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<DashboardRecentOrder>> GetRecentOrdersAsync(int branchId, DateTime startUtc, DateTime endUtc, int limit = 20)
    {
        EnsureUtcRange(startUtc, endUtc);

        var rows = await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                     && o.CreatedAt >= startUtc
                     && o.CreatedAt < endUtc)
            .OrderByDescending(o => o.CreatedAt)
            .Take(limit)
            .Select(o => new
            {
                o.OrderNumber,
                ItemCount = o.Items!.Sum(i => i.Quantity),
                o.TotalCents,
                o.KitchenStatusId,
                o.CancelledAt,
                o.CancellationReason,
                o.CreatedAt,
                Payments = o.Payments.Select(p => new { p.Method, p.AmountCents })
            })
            .ToListAsync();

        return rows.Select(o => new DashboardRecentOrder
        {
            OrderNumber = o.OrderNumber,
            ItemCount = o.ItemCount,
            TotalCents = o.TotalCents,
            KitchenStatus = o.KitchenStatusId switch { KitchenStatusIds.Pending => "Pending", KitchenStatusIds.Ready => "Ready", KitchenStatusIds.Delivered => "Delivered", _ => "Pending" },
            CancelledAt = o.CancelledAt,
            CancellationReason = o.CancellationReason,
            CreatedAt = o.CreatedAt,
            Payments = o.Payments.Select(p => new DashboardPayment
            {
                Method = p.Method.ToString(),
                AmountCents = p.AmountCents
            }).ToList()
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<List<OrphanedOrderDto>> GetOrphanedAsync(int branchId)
    {
        var rows = await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId && o.IsOrphaned)
            .OrderByDescending(o => o.CreatedAt)
            .Select(o => new
            {
                o.Id,
                o.OrderNumber,
                o.FolioNumber,
                o.CreatedAt,
                o.SyncedAt,
                o.TotalCents,
                o.PaidCents,
                o.IsPaid,
                o.TableName,
                o.CustomerId,
                CustomerFirstName = o.Customer != null ? o.Customer.FirstName : null,
                CustomerLastName = o.Customer != null ? o.Customer.LastName : null,
                ItemCount = o.Items!.Sum(i => i.Quantity),
                PaymentMethods = o.Payments.Select(p => p.Method).ToList(),
                o.ReconciliationNote
            })
            .ToListAsync();

        return rows.Select(o => new OrphanedOrderDto
        {
            Id = o.Id,
            OrderNumber = o.OrderNumber,
            FolioNumber = o.FolioNumber,
            CreatedAt = o.CreatedAt,
            SyncedAt = o.SyncedAt,
            TotalCents = o.TotalCents,
            PaidCents = o.PaidCents,
            IsPaid = o.IsPaid,
            TableName = o.TableName,
            CustomerId = o.CustomerId,
            CustomerName = o.CustomerFirstName == null
                ? null
                : (o.CustomerLastName == null ? o.CustomerFirstName : $"{o.CustomerFirstName} {o.CustomerLastName}"),
            ItemCount = o.ItemCount,
            PaymentMethods = o.PaymentMethods.Select(m => m.ToString()).Distinct().ToList(),
            ReconciliationNote = o.ReconciliationNote
        }).ToList();
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Defensive guard (BDD-013 VR-002). Rejects any non-UTC bound and collapsed
    /// ranges so a future caller that bypasses the service layer cannot re-introduce
    /// the <c>Npgsql Kind=Unspecified</c> class of 500 errors, nor silently return
    /// zero rows from a range where <c>startUtc &gt;= endUtc</c>.
    /// </summary>
    private static void EnsureUtcRange(DateTime startUtc, DateTime endUtc)
    {
        if (startUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("startUtc must have DateTimeKind.Utc", nameof(startUtc));
        if (endUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("endUtc must have DateTimeKind.Utc", nameof(endUtc));
        if (endUtc <= startUtc)
            throw new ArgumentException("endUtc must be strictly greater than startUtc", nameof(endUtc));
    }

    #endregion
}
