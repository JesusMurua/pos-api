using Microsoft.EntityFrameworkCore;
using POS.Domain.DTOs.Customer;
using POS.Domain.Enums;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository.IRepository;
using POS.Repository.Utils;

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

        // Aligned with GetSalesByPaymentMethodAsync (charts): paid, non-cancelled only.
        var totals = await _context.OrderPayments
            .AsNoTracking()
            .Where(p => p.Order.BranchId == branchId
                     && p.Order.CreatedAt >= startUtc
                     && p.Order.CreatedAt < endUtc
                     && p.Order.IsPaid
                     && p.Order.CancellationReason == null)
            .GroupBy(p => p.Method)
            .Select(g => new PaymentMethodTotalRow
            {
                Method = g.Key,
                TotalCents = g.Sum(p => p.AmountCents)
            })
            .ToListAsync();

        // Cash is stored as tendered; change is always returned in cash, so the
        // net cash collected = tendered − change.
        await SubtractCashChangeAsync(totals, branchId, startUtc, endUtc);
        return totals;
    }

    /// <summary>
    /// Sum of <c>Order.ChangeCents</c> for paid, non-cancelled orders in range —
    /// i.e. the cash handed back to customers. Always cash, so it nets out of the
    /// cash bucket of any payment-method breakdown.
    /// </summary>
    private Task<int> GetCashChangeTotalAsync(int branchId, DateTime startUtc, DateTime endUtc) =>
        _context.Orders.AsNoTracking()
            .Where(o => o.BranchId == branchId
                     && o.CreatedAt >= startUtc
                     && o.CreatedAt < endUtc
                     && o.IsPaid
                     && o.CancellationReason == null)
            .SumAsync(o => o.ChangeCents);

    private async Task SubtractCashChangeAsync(
        List<PaymentMethodTotalRow> totals, int branchId, DateTime startUtc, DateTime endUtc)
    {
        var change = await GetCashChangeTotalAsync(branchId, startUtc, endUtc);
        if (change == 0) return;

        var cash = totals.FirstOrDefault(t => t.Method == PaymentMethod.Cash);
        if (cash != null) cash.TotalCents -= change;
    }

    /// <inheritdoc />
    public async Task<List<TopProduct>> GetTopProductsAsync(int branchId, DateTime startUtc, DateTime endUtc, int top = 10)
    {
        EnsureUtcRange(startUtc, endUtc);

        // Materialize raw decimal aggregates first to avoid PostgreSQL banker's
        // rounding when EF Core translates Math.Round to SQL. The in-memory
        // (int)Math.Round(..., MidpointRounding.AwayFromZero) below preserves
        // half-away-from-zero semantics for cent totals.
        var rawRows = await _context.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.BranchId == branchId
                     && i.Order.CreatedAt >= startUtc
                     && i.Order.CreatedAt < endUtc
                     && i.Order.CancellationReason == null)
            .GroupBy(i => i.ProductName)
            .Select(g => new
            {
                Name = g.Key,
                Quantity = g.Sum(i => i.Quantity),
                RawTotalCents = g.Sum(i => i.Quantity * i.UnitPriceCents)
            })
            .OrderByDescending(g => g.Quantity)
            .Take(top)
            .ToListAsync();

        return rawRows.Select(r => new TopProduct
        {
            Name = r.Name,
            Quantity = r.Quantity,
            TotalCents = (int)Math.Round(r.RawTotalCents, MidpointRounding.AwayFromZero)
        }).ToList();
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
        int branchId, DateTime startUtc, DateTime endUtc, string granularity, string? timeZoneId)
    {
        EnsureUtcRange(startUtc, endUtc);

        // Fetch the (already date-bounded) rows, then bucket in memory by the
        // branch's LOCAL time. Grouping in SQL would bucket by UTC, shifting
        // late-night local sales onto the next day.
        var rows = await _context.Orders
            .AsNoTracking()
            .Where(o => o.BranchId == branchId
                     && o.IsPaid
                     && o.CancellationReason == null
                     && o.CreatedAt >= startUtc
                     && o.CreatedAt < endUtc)
            .Select(o => new { o.CreatedAt, o.TotalCents })
            .ToListAsync();

        var tz = TimeZoneHelper.GetTimeZoneInfo(timeZoneId);
        var unit = granularity?.ToLowerInvariant();

        DateTime BucketOf(DateTime createdAtUtc)
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(createdAtUtc, DateTimeKind.Utc), tz);
            return unit switch
            {
                "hour" => new DateTime(local.Year, local.Month, local.Day, local.Hour, 0, 0),
                "month" => new DateTime(local.Year, local.Month, 1),
                _ => new DateTime(local.Year, local.Month, local.Day)
            };
        }

        return rows
            .GroupBy(r => BucketOf(r.CreatedAt))
            .OrderBy(g => g.Key)
            .Select(g => new SalesPointDto
            {
                Date = g.Key,
                TotalCents = g.Sum(r => r.TotalCents),
                OrderCount = g.Count()
            })
            .ToList();
    }

    /// <inheritdoc />
    public async Task<List<TopProductDto>> GetTopProductsBIAsync(
        int branchId, DateTime startUtc, DateTime endUtc, int top = 10)
    {
        EnsureUtcRange(startUtc, endUtc);

        // Materialize raw decimal aggregates first to avoid PostgreSQL banker's
        // rounding when EF Core translates Math.Round to SQL. The in-memory
        // (int)Math.Round(..., MidpointRounding.AwayFromZero) below preserves
        // half-away-from-zero semantics for revenue cent totals.
        var rawRows = await _context.OrderItems
            .AsNoTracking()
            .Where(i => i.Order!.BranchId == branchId
                     && i.Order.IsPaid
                     && i.Order.CancellationReason == null
                     && i.Order.CreatedAt >= startUtc
                     && i.Order.CreatedAt < endUtc)
            .GroupBy(i => i.ProductName)
            .Select(g => new
            {
                ProductName = g.Key,
                QuantitySold = g.Sum(i => i.Quantity),
                RawTotalRevenueCents = g.Sum(i => i.Quantity * i.UnitPriceCents)
            })
            .OrderByDescending(g => g.QuantitySold)
            .Take(top)
            .ToListAsync();

        return rawRows.Select(r => new TopProductDto
        {
            ProductName = r.ProductName,
            QuantitySold = r.QuantitySold,
            TotalRevenueCents = (int)Math.Round(r.RawTotalRevenueCents, MidpointRounding.AwayFromZero)
        }).ToList();
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

        var rows = rawPayments.Select(r => new PaymentMethodSalesDto
        {
            PaymentMethod = r.Method.ToString(),
            Provider = r.Provider,
            TotalCents = r.TotalCents,
            TransactionCount = r.TransactionCount
        }).ToList();

        // Net out change from the cash bucket (tendered − change), same as the
        // summary. Change is always cash and has no provider.
        var change = await GetCashChangeTotalAsync(branchId, startUtc, endUtc);
        if (change != 0)
        {
            var cash = rows.FirstOrDefault(r =>
                r.PaymentMethod == PaymentMethod.Cash.ToString() && r.Provider == null);
            if (cash != null) cash.TotalCents -= change;
        }

        return rows;
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
                ItemCount = o.Items!.Count(),
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
                ItemCount = o.Items!.Count(),
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

    #region BDD-019 P4 — Customer-scoped read endpoints

    /// <inheritdoc />
    public async Task<PageData<CustomerOrderRowDto>> GetCustomerOrdersPagedAsync(
        int customerId, int page, int pageSize, DateTime? from, DateTime? to)
    {
        var query = _context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId);

        if (from.HasValue)
            query = query.Where(o => o.CreatedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(o => o.CreatedAt <= to.Value);

        // Count BEFORE projection so EF generates a single COUNT(*) without
        // hauling the columns of the page rows.
        var totalCount = await query.CountAsync();

        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(o => new CustomerOrderRowDto
            {
                OrderId = o.Id,
                OrderNumber = o.OrderNumber,
                CreatedAt = o.CreatedAt,
                TotalCents = o.TotalCents,
                ItemCount = o.Items!.Count(),
                BranchId = o.BranchId,
                BranchName = o.Branch != null ? o.Branch.Name : string.Empty,
                IsPaid = o.IsPaid,
                CancellationReason = o.CancellationReason
            })
            .ToListAsync();

        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

        return new PageData<CustomerOrderRowDto>
        {
            Data = rows,
            RowsCount = totalCount,
            TotalPages = totalPages,
            CurrentPage = page
        };
    }

    /// <inheritdoc />
    public async Task<CustomerStatsDto> GetCustomerStatsAsync(int customerId)
    {
        // Single SQL aggregation. Nullable casts protect against the no-rows
        // case where SUM/MAX would otherwise materialize as NULL → exception.
        var query = _context.Orders
            .AsNoTracking()
            .Where(o => o.CustomerId == customerId
                     && o.IsPaid
                     && o.CancellationReason == null);

        var stats = await query
            .GroupBy(_ => 1)
            .Select(g => new CustomerStatsDto
            {
                TotalSpentCents = g.Sum(o => (int?)o.TotalCents) ?? 0,
                OrderCount = g.Count(),
                LastOrderAt = g.Max(o => (DateTime?)o.CreatedAt)
            })
            .FirstOrDefaultAsync();

        return stats ?? new CustomerStatsDto
        {
            TotalSpentCents = 0,
            OrderCount = 0,
            LastOrderAt = null
        };
    }

    #endregion
}
