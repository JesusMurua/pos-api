# BDD-006b — Advanced BI Reporting & Performance Refactor
**Fase:** 17b | **Estado:** Diseno | **Fecha:** 2026-04-05

---

## 1. Current State vs Proposed State

### 1.1 Current State — Legacy Methods (THE PROBLEM)

The four original `ReportService` methods load **entire entity graphs into memory** before aggregating:

```
GetSummaryAsync()         → _unitOfWork.Orders.GetAsync(filter, "Items")
GenerateExcelAsync()      → calls GetSummaryAsync() internally
GeneratePdfAsync()        → calls GetSummaryAsync() internally
GenerateFiscalCsvAsync()  → _unitOfWork.Orders.GetAsync(filter, "Payments")
```

**What happens under the hood:**
1. EF Core generates `SELECT * FROM Orders LEFT JOIN OrderItems ...` with ALL columns
2. The change tracker allocates proxy objects for every row
3. All aggregation (SUM, COUNT, GroupBy) runs **in C# memory**, not SQL
4. For a tenant with 50K orders × 5 items avg = **250K tracked entities** per request

**Impact:**
| Metric | Current (10K orders) | Projected (2M orders) |
|---|---|---|
| Memory per request | ~120 MB | **OOM / process crash** |
| SQL transfer | ~45 MB | ~9 GB |
| Response time | ~3s | **timeout** |

### 1.2 Current State — BDD-006 Methods (ALREADY CORRECT)

The two BDD-006 methods (`GetDashboardChartsAsync`, `GetDetailedSalesCsvAsync`) are already implemented with the correct pattern:
- `_context.Orders.AsNoTracking().Where(...).Select(...)` — SQL-level projection
- `_context.OrderItems.AsNoTracking().GroupBy(...).Select(...)` — SQL-level aggregation
- No `.Include()` usage

**These methods require NO changes.**

### 1.3 Proposed State

Refactor the 4 legacy methods to use the same performant pattern as BDD-006:

| Method | Before | After |
|---|---|---|
| `GetSummaryAsync` | `GetAsync(filter, "Items")` → LINQ in memory | `_context` + `AsNoTracking()` + `GroupBy/Select` in SQL |
| `GenerateExcelAsync` | Calls `GetSummaryAsync` (inherits problem) | Calls refactored `GetSummaryAsync` (inherits fix) |
| `GeneratePdfAsync` | Calls `GetSummaryAsync` (inherits problem) | Calls refactored `GetSummaryAsync` (inherits fix) |
| `GenerateFiscalCsvAsync` | `GetAsync(filter, "Payments")` → all columns | `_context` + `AsNoTracking()` + `Select` projection |

**Result:** All 6 report methods will use `ApplicationDbContext` directly with `AsNoTracking()` + `Select()`. The `_unitOfWork` dependency becomes unused in `ReportService` and can be removed.

---

## 2. Files to Modify

| File | Action | Description |
|---|---|---|
| `POS.Services/Service/ReportService.cs` | **Modify** | Refactor `GetSummaryAsync` and `GenerateFiscalCsvAsync` to use `_context` with projections; remove `_unitOfWork` dependency |
| `POS.Services/IService/IReportService.cs` | **No change** | Interface contracts remain identical |
| `POS.API/Controllers/ReportController.cs` | **No change** | Endpoints remain identical |
| `POS.Domain/Models/ReportModels.cs` | **No change** | Existing DTOs (`ReportSummary`, `DailySummary`, `TopProduct`, `OrderReportRow`) already have the right shape |

> **No new files.** No new DTOs. No migrations. This is a pure internal refactor.

---

## 3. DTO Contracts (Existing — No Changes)

### 3.1 Report Summary DTOs (used by `GetSummaryAsync`, Excel, PDF)

```
ReportSummary
├── From: DateTime
├── To: DateTime
├── TotalOrders: int
├── CancelledOrders: int
├── CompletedOrders: int
├── TotalCents: int
├── CashCents: int
├── CardCents: int
├── TotalDiscountCents: int
├── AverageTicketCents: decimal
├── DailySummaries: List<DailySummary>
├── TopProducts: List<TopProduct>
└── Orders: List<OrderReportRow>

DailySummary
├── Date: DateTime
├── OrderCount: int
├── TotalCents: int
└── CancelledCount: int

TopProduct
├── Name: string
├── Quantity: int
└── TotalCents: int

OrderReportRow
├── OrderNumber: int
├── CreatedAt: DateTime
├── TotalCents: int
├── TotalDiscountCents: int
├── PaymentMethod: string
├── Status: string
├── CancellationReason: string?
└── ItemCount: int
```

### 3.2 BDD-006 DTOs (already implemented, no changes)

```
DashboardChartsDto
├── SalesOverTime: List<SalesPointDto>
├── TopProducts: List<TopProductDto>
└── SalesByPaymentMethod: List<PaymentMethodSalesDto>

SalesPointDto { Date, TotalCents, OrderCount }
TopProductDto { ProductName, QuantitySold, TotalRevenueCents }
PaymentMethodSalesDto { PaymentMethod, Provider?, TotalCents, TransactionCount }
```

---

## 4. Method Signatures — IReportService

**No signature changes.** The interface remains backward-compatible:

```csharp
public interface IReportService
{
    // Legacy — internal implementation refactored
    Task<ReportSummary> GetSummaryAsync(int branchId, DateTime from, DateTime to);
    Task<byte[]> GenerateExcelAsync(int branchId, DateTime from, DateTime to);
    Task<byte[]> GeneratePdfAsync(int branchId, DateTime from, DateTime to);
    Task<byte[]> GenerateFiscalCsvAsync(int branchId, DateTime from, DateTime to);

    // BDD-006 — already performant, no changes
    Task<DashboardChartsDto> GetDashboardChartsAsync(int branchId, DateTime from, DateTime to, string granularity);
    Task<string> GetDetailedSalesCsvAsync(int branchId, DateTime from, DateTime to);
}
```

---

## 5. SQL Execution Strategy

### 5.1 Refactored `GetSummaryAsync` — Query Decomposition

The current method runs ONE query that loads everything. The refactored version will use **4 independent, lightweight queries** against `_context`:

#### Query 1: Order-level metrics + daily summaries (single query)

```
_context.Orders.AsNoTracking()
  .Where(o => o.BranchId == branchId
           && o.CreatedAt.Date >= from.Date
           && o.CreatedAt.Date <= to.Date)
  .GroupBy(o => new { o.CreatedAt.Date, IsCancelled = o.CancellationReason != null })
  .Select(g => new {
      Date        = g.Key.Date,
      IsCancelled = g.Key.IsCancelled,
      OrderCount  = g.Count(),
      TotalCents  = g.Sum(o => o.TotalCents),
      DiscountCents = g.Sum(o => o.TotalDiscountCents)
  })
```

**SQL translation:** `SELECT CAST(CreatedAt AS date), CASE WHEN CancellationReason IS NOT NULL..., COUNT(*), SUM(TotalCents), SUM(TotalDiscountCents) FROM Orders WHERE ... GROUP BY ...`

From this single result set, we derive in-memory:
- `TotalOrders`, `CompletedOrders`, `CancelledOrders` (sum of counts)
- `TotalCents`, `TotalDiscountCents` (sum of completed groups)
- `AverageTicketCents` (TotalCents / CompletedOrders)
- `DailySummaries` list (one row per date)

#### Query 2: Cash/Card breakdown via Payments

```
_context.OrderPayments.AsNoTracking()
  .Where(p => p.Order.BranchId == branchId
           && p.Order.CreatedAt.Date >= from.Date
           && p.Order.CreatedAt.Date <= to.Date
           && p.Order.CancellationReason == null)
  .GroupBy(p => p.Method)
  .Select(g => new {
      Method    = g.Key,
      TotalCents = g.Sum(p => p.AmountCents)
  })
```

**SQL translation:** `SELECT Method, SUM(AmountCents) FROM OrderPayments JOIN Orders ... GROUP BY Method`

#### Query 3: Top 10 products

```
_context.OrderItems.AsNoTracking()
  .Where(i => i.Order.BranchId == branchId
           && i.Order.CreatedAt.Date >= from.Date
           && i.Order.CreatedAt.Date <= to.Date
           && i.Order.CancellationReason == null)
  .GroupBy(i => i.ProductName)
  .Select(g => new TopProduct {
      Name      = g.Key,
      Quantity  = g.Sum(i => i.Quantity),
      TotalCents = g.Sum(i => i.Quantity * i.UnitPriceCents)
  })
  .OrderByDescending(p => p.Quantity)
  .Take(10)
```

**SQL translation:** `SELECT TOP(10) ProductName, SUM(Quantity), SUM(Quantity * UnitPriceCents) FROM OrderItems JOIN Orders ... GROUP BY ProductName ORDER BY SUM(Quantity) DESC`

#### Query 4: Order rows (flat projection, no includes)

```
_context.Orders.AsNoTracking()
  .Where(o => o.BranchId == branchId
           && o.CreatedAt.Date >= from.Date
           && o.CreatedAt.Date <= to.Date)
  .OrderByDescending(o => o.CreatedAt)
  .Select(o => new OrderReportRow {
      OrderNumber       = o.OrderNumber,
      CreatedAt         = o.CreatedAt,
      TotalCents        = o.TotalCents,
      TotalDiscountCents = o.TotalDiscountCents,
      PaymentMethod     = string.Join(", ", o.Payments.Select(p => p.Method.ToString()).Distinct()),
      Status            = o.CancellationReason != null ? "Cancelada" : "Completada",
      CancellationReason = o.CancellationReason,
      ItemCount         = o.Items.Count
  })
```

**SQL translation:** EF Core translates `o.Items.Count` to a correlated subquery `(SELECT COUNT(*) FROM OrderItems WHERE OrderId = o.Id)` and `string.Join` on `Payments.Select(...)` may require client evaluation — see Section 5.3.

> **Note:** Queries 1-3 can run concurrently via `Task.WhenAll` since they are independent read-only operations on the same DbContext (EF Core supports concurrent reads with `AsNoTracking`).

### 5.2 Refactored `GenerateFiscalCsvAsync` — Flat Projection

```
_context.Orders.AsNoTracking()
  .Where(o => o.BranchId == branchId
           && o.CreatedAt.Date >= from.Date
           && o.CreatedAt.Date <= to.Date)
  .OrderByDescending(o => o.CreatedAt)
  .Select(o => new {
      o.Id,
      o.CreatedAt,
      o.TotalCents,
      PaymentMethods = o.Payments.Select(p => p.Method),
      o.InvoiceStatus
  })
```

**SQL translation:** `SELECT Id, CreatedAt, TotalCents, InvoiceStatus FROM Orders WHERE ...` + correlated subquery for Payments. The `string.Join("|", ...)` formatting happens in C# after materialization.

### 5.3 Client Evaluation Considerations

Two expressions may not fully translate to SQL:

| Expression | EF Core behavior | Mitigation |
|---|---|---|
| `string.Join(", ", o.Payments.Select(...))` | Client evaluation — EF fetches payment methods as sub-collection, joins in memory | Acceptable: only fetches `Method` column per payment, not full entity |
| `o.Items.Count` in Select | Translates to `(SELECT COUNT(*))` subquery | Fully SQL — no issue |

### 5.4 Performance Comparison

| Aspect | Before (Include) | After (Projection) |
|---|---|---|
| SQL columns fetched | ALL (~30 per Order + ~12 per Item) | Only needed (~5-8 per query) |
| Change tracker | Active (allocates proxies) | Disabled (`AsNoTracking`) |
| Aggregation location | C# memory (LINQ-to-Objects) | SQL Server (GROUP BY + SUM) |
| Network transfer (50K orders) | ~45 MB | ~2 MB |
| Memory per request (50K orders) | ~120 MB | ~5 MB |
| Concurrent queries | No (single large query) | Yes (Task.WhenAll for Q1-Q3) |

---

## 6. Constructor Change

### Before:
```csharp
public ReportService(IUnitOfWork unitOfWork, ApplicationDbContext context)
{
    _unitOfWork = unitOfWork;    // used by GetSummaryAsync, GenerateFiscalCsvAsync
    _context = context;          // used by BDD-006 methods
}
```

### After:
```csharp
public ReportService(ApplicationDbContext context)
{
    _context = context;          // used by ALL methods
}
```

The `IUnitOfWork` dependency is removed entirely since:
- No report method writes data (no `SaveChangesAsync` needed)
- All queries now use `_context` directly with `AsNoTracking()`
- DI registration in `Program.cs` will simplify automatically

---

## 7. Risk Assessment

| Risk | Severity | Mitigation |
|---|---|---|
| `string.Join` in `Select` causes full client evaluation | Medium | Test with EF Core query logging; if problematic, fetch payment methods as separate query |
| `CreatedAt.Date` not translatable in some SQL Server versions | Low | Already used in BDD-006 methods without issues; EF Core translates to `CONVERT(date, CreatedAt)` |
| Removing `_unitOfWork` breaks DI if other services share registration | Low | `ReportService` is registered independently; verify in `Program.cs` |
| Behavioral difference in edge cases (null Items collection) | Low | Current code already handles `o.Items?.Count ?? 0`; SQL `COUNT` returns 0 for no rows |
| Concurrent `Task.WhenAll` on same `DbContext` | Medium | EF Core `AsNoTracking` queries on same context are safe for concurrent reads; if any issue, fall back to sequential |

---

## 8. Validation Criteria

After implementation, each refactored method must:

1. **Zero `.Include()` calls** — grep confirms no Include usage in ReportService
2. **All queries use `.AsNoTracking()`** — verified in code review
3. **No `IUnitOfWork` dependency** — constructor only takes `ApplicationDbContext`
4. **Same JSON output** — response from `/api/report/summary` is identical before/after for a known dataset
5. **SQL verification** — EF Core query log shows `GROUP BY` and projected `SELECT` (not `SELECT *`)
6. **Memory baseline** — request for 10K orders uses < 10 MB (vs current ~120 MB)

---

## 9. Implementation Order

| Step | Description | Files |
|---|---|---|
| 1 | Refactor `GetSummaryAsync` to use 4 projected queries | `ReportService.cs` |
| 2 | Refactor `GenerateFiscalCsvAsync` to use flat projection | `ReportService.cs` |
| 3 | Remove `_unitOfWork` field and constructor parameter | `ReportService.cs` |
| 4 | Update DI registration if needed | `Program.cs` |
| 5 | Verify `GenerateExcelAsync` and `GeneratePdfAsync` work (they call `GetSummaryAsync`) | Manual test |
| 6 | Validate SQL output with EF Core logging | Runtime verification |
