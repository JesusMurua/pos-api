using POS.Domain.Enums;
using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IOrderRepository : IGenericRepository<Order>
{
    Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateTime date);

    Task<IEnumerable<Order>> GetPendingSyncAsync();

    Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateTime date);

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
    // ──────────────────────────────────────────

    /// <summary>
    /// Returns order-level metrics grouped by date and cancellation status.
    /// Uses AsNoTracking + GroupBy/Select for SQL-level aggregation.
    /// </summary>
    Task<List<OrderDailyMetricRow>> GetDailyMetricsAsync(int branchId, DateTime from, DateTime to);

    /// <summary>
    /// Returns payment totals grouped by payment method for completed orders.
    /// Uses AsNoTracking + GroupBy/Select for SQL-level aggregation.
    /// </summary>
    Task<List<PaymentMethodTotalRow>> GetPaymentTotalsAsync(int branchId, DateTime from, DateTime to);

    /// <summary>
    /// Returns top N products by quantity sold for completed orders.
    /// Uses AsNoTracking + GroupBy/Select for SQL-level aggregation.
    /// </summary>
    Task<List<TopProduct>> GetTopProductsAsync(int branchId, DateTime from, DateTime to, int top = 10);

    /// <summary>
    /// Returns flat order rows with item count and payment methods, without loading navigation properties.
    /// Uses AsNoTracking + Select projection for SQL-level column reduction.
    /// </summary>
    Task<List<OrderReportRow>> GetFlatOrderRowsAsync(int branchId, DateTime from, DateTime to);

    /// <summary>
    /// Returns flat order projections for fiscal CSV export without loading full entities.
    /// Uses AsNoTracking + Select for SQL-level projection.
    /// </summary>
    Task<List<FiscalCsvRow>> GetFlatOrdersForCsvAsync(int branchId, DateTime from, DateTime to);

    /// <summary>
    /// Returns sales data grouped by time bucket for the BI dashboard.
    /// Uses AsNoTracking + GroupBy/Select for SQL-level aggregation.
    /// </summary>
    Task<List<SalesPointDto>> GetSalesOverTimeAsync(int branchId, DateTime from, DateTime to, string granularity);

    /// <summary>
    /// Returns top N products with revenue for the BI dashboard.
    /// Uses AsNoTracking + GroupBy/Select for SQL-level aggregation.
    /// </summary>
    Task<List<TopProductDto>> GetTopProductsBIAsync(int branchId, DateTime from, DateTime to, int top = 10);

    /// <summary>
    /// Returns payment breakdown by method and provider for the BI dashboard.
    /// Uses AsNoTracking + GroupBy/Select for SQL-level aggregation.
    /// </summary>
    Task<List<PaymentMethodSalesDto>> GetSalesByPaymentMethodAsync(int branchId, DateTime from, DateTime to);

    /// <summary>
    /// Returns flat order projections for the detailed sales CSV export.
    /// Uses AsNoTracking + Select for SQL-level projection with Customer join.
    /// </summary>
    Task<List<DetailedSalesCsvRow>> GetDetailedSalesCsvRowsAsync(int branchId, DateTime from, DateTime to);
}
