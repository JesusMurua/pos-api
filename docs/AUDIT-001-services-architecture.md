# AUDIT-001 — Architectural Audit: POS.Services & Controllers
**Date:** 2026-04-05 | **Scope:** Clean Architecture, Repository Pattern, Performance | **Status:** Violations Found

---

## Verdict: VIOLATIONS FOUND

4 audit categories were checked. Issues found in 3 of 4 categories.

| Category | Status | Count |
|---|---|---|
| 1. Direct DbContext Injection in Services | CLEAN | 0 |
| 2. N+1 Query Anti-patterns | VIOLATIONS | 3 |
| 3. Abuse of `.Include()` in Read-Only Aggregations | VIOLATIONS | 3 |
| 4. Business Logic in Controllers | VIOLATIONS | 4 HIGH, 5 MEDIUM |

---

## Category 1: Direct DbContext Injection — CLEAN

No service in `POS.Services/Service/` injects `ApplicationDbContext`. All services depend exclusively on `IUnitOfWork`, `ILogger`, or external interfaces (e.g., `IEmailService`, `IStripeClient`).

---

## Category 2: N+1 Query Anti-patterns

### V-2.1 — ProductImportService.cs → `ImportAsync`

| Field | Detail |
|---|---|
| **File** | `POS.Services/Service/ProductImportService.cs` |
| **Method** | `ImportAsync` |
| **Lines** | ~191–212 |
| **Pattern** | `foreach (row in rows) { await _unitOfWork.Categories.AddAsync(...); await _unitOfWork.SaveChangesAsync(); }` |
| **Type** | `SaveChangesAsync` inside loop |
| **Why it's bad** | Each new category triggers a separate SQL transaction. For a CSV with 50 new categories, this is 50 round-trips instead of 1 batch insert + 1 commit. |
| **Fix** | Collect new categories in a list, call `AddAsync` for each, then `SaveChangesAsync` once after the loop. |

### V-2.2 — StockReceiptService.cs → `CreateAsync`

| Field | Detail |
|---|---|
| **File** | `POS.Services/Service/StockReceiptService.cs` |
| **Method** | `CreateAsync` |
| **Lines** | ~80–107 |
| **Pattern** | `foreach (item in request.Items) { await ProcessInventoryItemReceiptAsync(...); }` where helper calls `GetByIdAsync` + `AddAsync` |
| **Type** | N+1 reads (one `GetByIdAsync` per line item) |
| **Why it's bad** | A receipt with 30 line items generates 30 individual `SELECT` queries. Should batch-fetch all inventory items / products before the loop using `WHERE Id IN (...)`. |
| **Fix** | Pre-fetch all referenced items with a single `GetAsync(i => ids.Contains(i.Id))`, build a dictionary, then loop over in-memory lookups. |

### V-2.3 — OrderService.cs → `RecordPromotionUsagesAsync`

| Field | Detail |
|---|---|
| **File** | `POS.Services/Service/OrderService.cs` |
| **Method** | `RecordPromotionUsagesAsync` |
| **Lines** | ~1251–1266 |
| **Pattern** | `foreach (item in order.Items.Where(i => i.PromotionId.HasValue)) { var promo = await _unitOfWork.Promotions.GetByIdAsync(promoId); }` |
| **Type** | N+1 reads |
| **Why it's bad** | One `SELECT` per distinct promotion ID. Mitigated by `recordedIds` deduplication but still generates N queries for N distinct promotions on an order. |
| **Fix** | Collect unique `PromotionId` values first, fetch all in one query, validate from dictionary. |

---

## Category 3: Abuse of `.Include()` in Read-Only Aggregations

### V-3.1 — DashboardService.cs → `GetSummaryAsync` (CRITICAL)

| Field | Detail |
|---|---|
| **File** | `POS.Services/Service/DashboardService.cs` |
| **Method** | `GetSummaryAsync` |
| **Lines** | 22–25 |
| **Code** | `_unitOfWork.Orders.GetAsync(filter, "Items,Payments")` |
| **Why it's bad** | Loads ALL orders for a day with ALL their Items and Payments into tracked memory. This is the **exact same anti-pattern** we just fixed in `ReportService`. For a busy restaurant with 500 orders/day × 5 items × 2 payments = ~3,500 tracked entities per request. All aggregation (SUM, COUNT, GroupBy, TopProducts) runs in C# memory. Dashboard is called frequently (every page load). |
| **Severity** | **CRITICAL** — This is the most-called endpoint and mirrors the ReportService problem. |
| **Fix** | Same pattern as BDD-006b: add repository projection methods for dashboard metrics (`GetDashboardMetricsAsync`) and call via `_unitOfWork.Orders`. |

### V-3.2 — CashRegisterService.cs → Cash total calculation

| Field | Detail |
|---|---|
| **File** | `POS.Services/Service/CashRegisterService.cs` |
| **Method** | (cash total helper, ~line 184) |
| **Lines** | 184–194 |
| **Code** | `_unitOfWork.Orders.GetAsync(filter, "Payments")` then `.SelectMany(o => o.Payments).Where(Cash).Sum()` |
| **Why it's bad** | Loads entire orders with all payments to compute a single `SUM(AmountCents) WHERE Method = 'Cash'`. This could be a single SQL query: `SELECT SUM(AmountCents) FROM OrderPayments JOIN Orders ... WHERE Method = 0`. |
| **Severity** | **HIGH** — Called on every cash register close. Volume scales with session length. |
| **Fix** | Add a `GetCashTotalForSessionAsync(branchId, from, to)` projection method to `IOrderRepository`. |

### V-3.3 — OrderService.cs → `GetActiveByTableAsync`

| Field | Detail |
|---|---|
| **File** | `POS.Services/Service/OrderService.cs` |
| **Method** | `GetActiveByTableAsync` |
| **Lines** | 626–630 |
| **Code** | `_unitOfWork.Orders.GetAsync(o => o.TableId == tableId && ... , "Items")` |
| **Why it's bad** | Loads full Item entities for active table orders when only a projected subset is returned (the method returns anonymous objects with select fields). The Include("Items") is used only to count/summarize but EF loads all columns of all items. |
| **Severity** | **MEDIUM** — Impact is bounded by active orders per table (typically 1–3), but it's architecturally incorrect. |
| **Fix** | Use a repository projection that only fetches the required fields. |

---

## Category 4: Business Logic in Controllers

### V-4.1 — PrintJobController.cs → `MarkPrinted` / `MarkFailed` (HIGH)

| Field | Detail |
|---|---|
| **File** | `POS.API/Controllers/PrintJobController.cs` |
| **Methods** | `MarkPrinted` (line 110), `MarkFailed` (line 141) |
| **Violation** | Direct `_unitOfWork` access: `GetByIdAsync`, `Update`, `SaveChangesAsync`. Business logic: status transitions, attempt counting, max-attempts threshold check. |
| **Why it's bad** | Controller performs entity state mutations, business rule enforcement (max retries), and persistence. This should be a `IPrintJobService.MarkPrintedAsync(id, branchId)` call. |

### V-4.2 — StripeWebhookController.cs → `HandleWebhook` (HIGH)

| Field | Detail |
|---|---|
| **File** | `POS.API/Controllers/StripeWebhookController.cs` |
| **Method** | `HandleWebhook` (line ~41) |
| **Violation** | Direct `_unitOfWork.StripeEventInbox.AddAsync` + `SaveChangesAsync`. Stripe event validation, inbox entity creation, and duplicate detection via `DbUpdateException` — all in the controller. |
| **Why it's bad** | Webhook processing is business logic. Error handling for duplicates via catching `DbUpdateException` is a data-layer concern that should never be in a controller. |

### V-4.3 — CatalogController.cs → All methods (HIGH)

| Field | Detail |
|---|---|
| **File** | `POS.API/Controllers/CatalogController.cs` |
| **Methods** | All 7 endpoints |
| **Violation** | Controller injects `IUnitOfWork` directly and calls `_unitOfWork.Catalog.*` for every endpoint. No service layer. |
| **Why it's bad** | Bypasses the service layer entirely. If business rules ever need to be added to catalog queries (e.g., filtering by plan type), they'd have to go in the controller. |
| **Note** | Low risk given these are simple read-only lookups, but architecturally inconsistent. |

### V-4.4 — DeliveryController.cs → `Webhook` (HIGH)

| Field | Detail |
|---|---|
| **File** | `POS.API/Controllers/DeliveryController.cs` |
| **Method** | `Webhook` (line ~186) |
| **Violation** | Direct `_unitOfWork.BranchDeliveryConfigs` access + webhook secret validation logic in controller. |
| **Why it's bad** | Security-critical authentication logic (comparing webhook secrets) lives in the controller instead of a service. |

### V-4.5 — ProductsController.cs → `UpdateStock` (MEDIUM)

| Field | Detail |
|---|---|
| **File** | `POS.API/Controllers/ProductsController.cs` |
| **Method** | `UpdateStock` (line ~189) |
| **Violation** | Stock adjustment calculation (`+=`, `-=`, `=` with switch/case), availability threshold logic, and negative-stock clamping — all in the controller. |
| **Why it's bad** | Stock business rules (in/out/adjustment types, threshold-based availability) should be in `IProductService`. |

### V-4.6 — OrdersController.cs → `AddPayment` (MEDIUM)

| Field | Detail |
|---|---|
| **File** | `POS.API/Controllers/OrdersController.cs` |
| **Method** | `AddPayment` (line ~217) |
| **Violation** | Creates `OrderPayment` entity, calls `GetByBranchAndDateAsync`, calculates `paidCents`, `changeCents`, `remainingCents` in the controller. |
| **Why it's bad** | Payment creation and balance calculations are core business logic. |

### V-4.7 — TableController.cs → `UpdateStatus` (MEDIUM)

| Field | Detail |
|---|---|
| **File** | `POS.API/Controllers/TableController.cs` |
| **Method** | `UpdateStatus` (line ~127) |
| **Violation** | Checks active orders before allowing table status change to "available". Business rule validation in controller. |

### V-4.8 — BranchController.cs → `UpdateFolioConfig` / `UpdateSettings` (MEDIUM)

| Field | Detail |
|---|---|
| **File** | `POS.API/Controllers/BranchController.cs` |
| **Methods** | `UpdateFolioConfig` (~line 237), `UpdateSettings` (~line 262) |
| **Violation** | Fetches entity, manually sets properties, calls update. The property mapping should be encapsulated in the service. |

### V-4.9 — BusinessController.cs → `UpdateFiscalConfig` (MEDIUM)

| Field | Detail |
|---|---|
| **File** | `POS.API/Controllers/BusinessController.cs` |
| **Method** | `UpdateFiscalConfig` (~line 108) |
| **Violation** | RFC normalization (`Trim().ToUpperInvariant()`) in controller. Data transformation is business logic. |

---

## Priority Matrix

| Priority | Violations | Rationale |
|---|---|---|
| **P0 — Fix now** | V-3.1 (DashboardService Include) | Most-called endpoint, same OOM risk pattern we just fixed in ReportService |
| **P1 — Fix soon** | V-3.2 (CashRegister Include), V-4.1 (PrintJob controller logic), V-4.2 (Stripe webhook controller logic) | Performance risk + architecture violation in critical paths |
| **P2 — Next sprint** | V-2.1 (Import SaveChanges loop), V-2.2 (StockReceipt N+1), V-2.3 (Promotion N+1), V-3.3 (Table orders Include), V-4.3 (Catalog controller), V-4.4 (Delivery webhook) | Real but bounded impact |
| **P3 — Backlog** | V-4.5 to V-4.9 (Medium controller logic) | Architectural debt, not performance risk |
