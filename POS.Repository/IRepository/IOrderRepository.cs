using POS.Domain.DTOs.Customer;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository.Utils;

namespace POS.Repository.IRepository;

public interface IOrderRepository : IGenericRepository<Order>
{
    /// <summary>
    /// Returns orders whose <c>CreatedAt</c> falls in the half-open UTC range
    /// <c>[startUtc, endUtc)</c>. Both bounds must have <see cref="DateTimeKind.Utc"/>.
    /// </summary>
    Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateTime startUtc, DateTime endUtc);

    Task<IEnumerable<Order>> GetPendingSyncAsync();

    /// <summary>
    /// Same window contract as <see cref="GetByBranchAndDateAsync"/>; excludes orders
    /// whose <c>SyncStatusId</c> is <c>Failed</c>.
    /// </summary>
    Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateTime startUtc, DateTime endUtc);

    /// <summary>
    /// Returns orders updated since a timestamp with Items and Payments included.
    /// </summary>
    Task<IEnumerable<Order>> GetPullOrdersAsync(int branchId, DateTime since);

    /// <summary>
    /// Gets all active delivery orders for a branch (not PickedUp or Rejected).
    /// </summary>
    Task<IEnumerable<Order>> GetActiveDeliveryOrdersAsync(int branchId);

    /// <summary>
    /// Gets a single order by its external platform ID.
    /// </summary>
    Task<Order?> GetByExternalIdAsync(int branchId, string externalOrderId);

    // ──────────────────────────────────────────
    // BDD-006b: High-performance report projections
    // Every per-day / per-range method below expects UTC bounds pinned by the
    // service layer via TimeZoneHelper.GetUtcRangeForLocalDate(...).
    // ──────────────────────────────────────────

    /// <summary>
    /// Returns order-level metrics grouped by date and cancellation status.
    /// Uses AsNoTracking + GroupBy/Select for SQL-level aggregation.
    /// </summary>
    Task<List<OrderDailyMetricRow>> GetDailyMetricsAsync(int branchId, DateTime startUtc, DateTime endUtc);

    /// <summary>
    /// Returns payment totals grouped by payment method for completed orders.
    /// Uses AsNoTracking + GroupBy/Select for SQL-level aggregation.
    /// </summary>
    Task<List<PaymentMethodTotalRow>> GetPaymentTotalsAsync(int branchId, DateTime startUtc, DateTime endUtc);

    /// <summary>
    /// Returns top N products by quantity sold for completed orders.
    /// Uses AsNoTracking + GroupBy/Select for SQL-level aggregation.
    /// </summary>
    Task<List<TopProduct>> GetTopProductsAsync(int branchId, DateTime startUtc, DateTime endUtc, int top = 10);

    /// <summary>
    /// Returns flat order rows with item count and payment methods, without loading navigation properties.
    /// Uses AsNoTracking + Select projection for SQL-level column reduction.
    /// </summary>
    Task<List<OrderReportRow>> GetFlatOrderRowsAsync(int branchId, DateTime startUtc, DateTime endUtc);

    /// <summary>
    /// Returns flat order projections for fiscal CSV export without loading full entities.
    /// Uses AsNoTracking + Select for SQL-level projection.
    /// </summary>
    Task<List<FiscalCsvRow>> GetFlatOrdersForCsvAsync(int branchId, DateTime startUtc, DateTime endUtc);

    /// <summary>
    /// Returns sales data grouped by time bucket for the BI dashboard. Buckets are
    /// computed in the branch's local timezone, so a late-night sale lands on the
    /// correct local day rather than the next UTC day.
    /// </summary>
    Task<List<SalesPointDto>> GetSalesOverTimeAsync(int branchId, DateTime startUtc, DateTime endUtc, string granularity, string? timeZoneId);

    /// <summary>
    /// Returns top N products with revenue for the BI dashboard.
    /// Uses AsNoTracking + GroupBy/Select for SQL-level aggregation.
    /// </summary>
    Task<List<TopProductDto>> GetTopProductsBIAsync(int branchId, DateTime startUtc, DateTime endUtc, int top = 10);

    /// <summary>
    /// Returns payment breakdown by method and provider for the BI dashboard.
    /// Uses AsNoTracking + GroupBy/Select for SQL-level aggregation.
    /// </summary>
    Task<List<PaymentMethodSalesDto>> GetSalesByPaymentMethodAsync(int branchId, DateTime startUtc, DateTime endUtc);

    /// <summary>
    /// Returns flat order projections for the detailed sales CSV export.
    /// Uses AsNoTracking + Select for SQL-level projection with Customer join.
    /// </summary>
    Task<List<DetailedSalesCsvRow>> GetDetailedSalesCsvRowsAsync(int branchId, DateTime startUtc, DateTime endUtc);

    // ──────────────────────────────────────────
    // AUDIT-001 P0: Dashboard projections
    // ──────────────────────────────────────────

    /// <summary>
    /// Returns cancellation metrics grouped by reason for a single day.
    /// Uses AsNoTracking + GroupBy/Select for SQL-level aggregation.
    /// </summary>
    Task<List<CancellationReasonRow>> GetCancellationsByReasonAsync(int branchId, DateTime startUtc, DateTime endUtc);

    /// <summary>
    /// Returns the N most recent orders as flat projections with item count and payments.
    /// Uses AsNoTracking + Select for SQL-level projection — no .Include().
    /// </summary>
    Task<List<DashboardRecentOrder>> GetRecentOrdersAsync(int branchId, DateTime startUtc, DateTime endUtc, int limit = 20);

    /// <summary>
    /// Returns orphaned orders for a branch as flat projections.
    /// Uses AsNoTracking + Select for SQL-level projection — no .Include().
    /// </summary>
    Task<List<OrphanedOrderDto>> GetOrphanedAsync(int branchId);

    // ──────────────────────────────────────────
    // BDD-019 P4: Customer-scoped read endpoints
    // ──────────────────────────────────────────

    /// <summary>
    /// Returns paginated orders for a single customer projected as
    /// <see cref="CustomerOrderRowDto"/>. Pure SQL projection: no entity
    /// hydration and no JSON columns loaded. Sorted by <c>CreatedAt</c> desc.
    /// </summary>
    /// <param name="customerId">Owner of the orders (caller must have validated tenant ownership).</param>
    /// <param name="page">1-based page index.</param>
    /// <param name="pageSize">Page size, expected to be in [1, 100].</param>
    /// <param name="from">Inclusive lower bound on <c>CreatedAt</c> (UTC). Null = no lower bound.</param>
    /// <param name="to">Inclusive upper bound on <c>CreatedAt</c> (UTC). Null = no upper bound.</param>
    Task<PageData<CustomerOrderRowDto>> GetCustomerOrdersPagedAsync(
        int customerId, int page, int pageSize, DateTime? from, DateTime? to);

    /// <summary>
    /// Returns aggregated stats for the given customer using a single
    /// DB-level aggregation (SUM + COUNT + MAX). Filters to paid,
    /// non-cancelled orders. Null SUM/MAX are coalesced (zero / null) so the
    /// projection materializes safely for customers with no orders.
    /// </summary>
    Task<CustomerStatsDto> GetCustomerStatsAsync(int customerId);
}
