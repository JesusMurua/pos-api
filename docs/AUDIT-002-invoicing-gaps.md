# AUDIT-002 — CFDI 4.0 Invoicing Gap Analysis
**Date:** 2026-04-05 | **Scope:** Phase 15 — Electronic Invoicing with Facturapi | **Status:** Gaps Found
**Design docs:** [BDD-004](BDD-004-electronic-invoicing.md), [BDD-004-15d](BDD-004-15d-public-invoicing-api.md)

---

## 1. What's ALREADY IMPLEMENTED (Functional)

### 1.1 Domain Models & Schema (Subfase 15a — DONE)

| Component | Status | File |
|---|---|---|
| `Business` fiscal fields (`Rfc`, `TaxRegime`, `LegalName`, `InvoicingEnabled`, `FacturapiOrganizationId`) | DONE | `POS.Domain/Models/Business.cs` |
| `Product` SAT fields (`SatProductCode`, `SatUnitCode`, `TaxRate`) | DONE | `POS.Domain/Models/Product.cs` |
| `Order` invoice fields (`InvoiceStatus`, `FacturapiId`, `InvoiceUrl`, `InvoicedAt`, `FiscalCustomerId`) | DONE | `POS.Domain/Models/Order.cs` |
| `InvoiceStatus` enum (`None`, `Pending`, `Issued`, `Cancelled`) | DONE | `POS.Domain/Enums/InvoiceStatus.cs` |
| `FiscalCustomer` entity (full model with RFC, regime, Facturapi ID, CRM link) | DONE | `POS.Domain/Models/FiscalCustomer.cs` |
| `FacturapiSettings` config class (`ApiKey`, `WebhookSecret`, `IsSandbox`) | DONE | `POS.Domain/Settings/FacturapiSettings.cs` |
| EF migration for all schema changes | DONE | Applied |

### 1.2 Repository Layer (Subfase 15b partial — DONE)

| Component | Status | File |
|---|---|---|
| `IFiscalCustomerRepository` (`GetByRfcAsync`, `GetByBusinessAsync`) | DONE | `POS.Repository/IRepository/IFiscalCustomerRepository.cs` |
| `FiscalCustomerRepository` implementation | DONE | `POS.Repository/Repository/FiscalCustomerRepository.cs` |
| `IUnitOfWork.FiscalCustomers` property | DONE | `POS.Repository/IUnitOfWork.cs` |
| `DbSet<FiscalCustomer>` in ApplicationDbContext | DONE | `POS.Repository/ApplicationDbContext.cs` |

### 1.3 Service Layer (Subfases 15c/15d — PARTIAL)

| Component | Status | File |
|---|---|---|
| `IInvoicingService` interface (4 methods + DTOs) | DONE | `POS.Services/IService/IInvoicingService.cs` |
| `InvoicingService` implementation (all 4 methods) | **MOCK** | `POS.Services/Service/InvoicingService.cs` |
| `CreateGlobalInvoiceAsync` — fetches uninvoiced orders, updates status | **MOCK** — generates fake `fpi_global_` ID |
| `RequestIndividualInvoiceAsync` — validates order, links FiscalCustomer | **MOCK** — generates fake `fpi_ind_` ID |
| `GetPublicOrderDetailsAsync` — receipt proof + safe DTO | DONE (fully functional) |
| `RequestPublicInvoiceAsync` — upsert FiscalCustomer + delegates | **MOCK** (delegates to mocked individual method) |

### 1.4 API Layer (Subfases 15c/15d — DONE)

| Component | Status | File |
|---|---|---|
| `InvoicingController` (`POST /global`, `POST /individual`) | DONE | `POS.API/Controllers/InvoicingController.cs` |
| `PublicInvoicingController` (`GET /public/{orderId}`, `POST /public/request`) | DONE | `POS.API/Controllers/PublicInvoicingController.cs` |
| Rate limiting `PublicInvoicingPolicy` (10/min per IP) | DONE | `POS.API/Program.cs` |
| Receipt proof validation (`TotalCents` match) | DONE | `InvoicingService.cs` |

---

## 2. GAPS — What's Missing, Mocked, or Incomplete

### G-1: Real Facturapi HTTP Calls (CRITICAL)

| Field | Detail |
|---|---|
| **File** | `POS.Services/Service/InvoicingService.cs` |
| **Lines** | 44–46 and 91–93 |
| **Code** | `// TODO: Call Facturapi API to create global invoice` → `var mockFacturapiId = $"fpi_global_{Guid.NewGuid():N}"[..30];` |
| **Impact** | No real CFDI is ever created. InvoiceStatus is set to `Pending` but never transitions to `Issued` because there's no webhook processing. Orders appear "pending" forever. |
| **What's needed** | Replace mock IDs with real HTTP calls to Facturapi REST API: `POST /v2/invoices` for individual, `POST /v2/invoices` with `type: "global"` for global. Must use `HttpClient` + `FacturapiSettings.ApiKey` (or per-business Organization key). |

### G-2: Invoice Entity & InvoiceOrder Pivot Table (HIGH)

| Field | Detail |
|---|---|
| **Designed in** | BDD-004, Sections 4.7 and 4.8 |
| **Current state** | NOT IMPLEMENTED — no `Invoice.cs`, no `InvoiceOrder.cs` model in `POS.Domain/Models/` |
| **Impact** | There's no dedicated invoice record in the database. Invoice data is only stored as fields on the `Order` entity (`FacturapiId`, `InvoiceStatus`). This means: (a) no N:N relationship between invoices and orders (global invoices cover multiple orders), (b) no place to store CFDI metadata (series, folio, PDF/XML URLs, subtotal/tax breakdown, cancellation reason), (c) no way to query "all invoices for this branch in March". |
| **What's needed** | Create `Invoice` and `InvoiceOrder` entities per BDD-004 Section 4.7/4.8. Migration to add both tables. Update `InvoicingService` to create `Invoice` records alongside updating `Order` fields. |

### G-3: OrderItem Fiscal Fields (HIGH)

| Field | Detail |
|---|---|
| **Designed in** | BDD-004, Section 4.4 |
| **Current state** | NOT IMPLEMENTED — `OrderItem.cs` has no `TaxRatePercent`, `TaxAmountCents`, `SatProductCode`, `SatUnitCode` fields |
| **Impact** | When a CFDI is created, the invoice line items need SAT codes and tax breakdown. Currently there's no way to freeze the fiscal data at the time of the sale. The `InvoicingService` would have to look up the current Product data at invoice time, which could have changed since the sale. |
| **What's needed** | Add 4 fields to `OrderItem`: `TaxRatePercent` (decimal?), `TaxAmountCents` (int, default 0), `SatProductCode` (string?, 10), `SatUnitCode` (string?, 5). Migration. Populate during sync or at invoice creation time (lazy enrichment per BDD-004 Section 10.2). |

### G-4: Facturapi Webhook Processing (HIGH)

| Field | Detail |
|---|---|
| **Designed in** | BDD-004, Section 9 |
| **Current state** | NOT IMPLEMENTED — no `FacturapiWebhookInbox` entity, no `FacturapiWebhookController`, no background worker |
| **Impact** | Once a real CFDI is submitted to Facturapi, its status changes asynchronously (pending → valid, or pending → error). Without webhook processing, the system cannot: (a) update `InvoiceStatus` from `Pending` to `Issued`, (b) populate `InvoiceUrl` for PDF/XML downloads, (c) handle errors or cancellations from SAT. |
| **What's needed** | Follow the Stripe webhook pattern: create `FacturapiWebhookInbox` entity + repository, `FacturapiWebhookController` (AllowAnonymous, validates signature), and `FacturapiWebhookProcessorWorker` (hosted service that polls pending events). |

### G-5: SatPaymentForm Helper (MEDIUM)

| Field | Detail |
|---|---|
| **Designed in** | BDD-004, Section 4.10 |
| **Current state** | NOT IMPLEMENTED — no `SatPaymentForm.cs` helper in `POS.Domain/Helpers/` |
| **Impact** | CFDI 4.0 requires a `FormaDePago` field (e.g., `01` for cash, `04` for card). There's no mapping from our `PaymentMethod` enum to SAT payment form codes. The `InvoicingService` would need this when building invoice items for Facturapi. |
| **What's needed** | Create `POS.Domain/Helpers/SatPaymentForm.cs` with a static `MapFromPaymentMethod(PaymentMethod method)` method per BDD-004 Section 4.10. |

### G-6: Invoice Cancellation Endpoint (MEDIUM)

| Field | Detail |
|---|---|
| **Designed in** | BDD-004, Section 6.3 |
| **Current state** | NOT IMPLEMENTED — no `POST /api/invoicing/{id}/cancel` endpoint, no `CancelInvoiceAsync` method |
| **Impact** | Once an invoice is issued, there's no way to cancel it via the API. Mexican tax law requires cancellation within 24 hours (or with receptor acceptance). Without this, businesses cannot correct invoicing errors. |
| **What's needed** | Add `CancelInvoiceAsync(int invoiceId, string reason)` to `IInvoicingService`. Add endpoint to `InvoicingController`. Requires `Invoice` entity (G-2) to track cancellation reason and timestamp. |

### G-7: Invoice Query/Download Endpoints (MEDIUM)

| Field | Detail |
|---|---|
| **Designed in** | BDD-004, Section 6.3 |
| **Current state** | NOT IMPLEMENTED — no `GET /api/invoicing/{id}`, `GET /api/invoicing/{id}/download/{format}`, or `GET /api/invoicing/by-order/{orderId}` endpoints |
| **Impact** | No way for the frontend to list invoices, view invoice details, or download PDF/XML. The `InvoiceUrl` field on `Order` is never populated (since webhooks don't work). |
| **What's needed** | Requires `Invoice` entity (G-2) + Facturapi webhook processing (G-4) to populate URLs. Add query endpoints to `InvoicingController`. |

### G-8: Global Invoice Auto-Trigger on Cash Register Close (LOW)

| Field | Detail |
|---|---|
| **Designed in** | BDD-004, Section 8 (implied) |
| **Current state** | NOT IMPLEMENTED — `CashRegisterService` and `OrderService` have zero invoicing logic |
| **Impact** | SAT requires that uninvoiced sales be consolidated in a "Factura Global" periodically (monthly at minimum). Currently there's no automated trigger — the manager must manually call `POST /api/invoicing/global`. |
| **What's needed** | Optional: add a flag to cash register close (`CashRegisterService.CloseAsync`) that triggers `CreateGlobalInvoiceAsync` for the session period. Or implement as a monthly scheduled job. Lower priority since the manual endpoint exists. |

### G-9: FacturapiSettings Not Configured in appsettings (LOW)

| Field | Detail |
|---|---|
| **Current state** | `FacturapiSettings` class exists but no `"Facturapi"` section in `appsettings.json`, `appsettings.Development.json`, or `appsettings.Production.json` |
| **Impact** | The settings class would bind to empty defaults. `ApiKey` would be null, `IsSandbox` would be true. |
| **What's needed** | Add `"Facturapi": { "ApiKey": "", "WebhookSecret": "", "IsSandbox": true }` to appsettings.json. Register `IOptions<FacturapiSettings>` in `Program.cs` if not already done. |

---

## 3. Gap Summary Matrix

| Gap | Severity | Depends On | Description |
|---|---|---|---|
| **G-1** | CRITICAL | G-5 | Replace mock Facturapi calls with real HTTP calls |
| **G-2** | HIGH | — | Create `Invoice` + `InvoiceOrder` entities + migration |
| **G-3** | HIGH | — | Add fiscal fields to `OrderItem` + migration |
| **G-4** | HIGH | G-2 | Facturapi webhook inbox + controller + background worker |
| **G-5** | MEDIUM | — | `SatPaymentForm` helper (PaymentMethod → SAT code mapping) |
| **G-6** | MEDIUM | G-1, G-2 | Invoice cancellation endpoint + Facturapi API call |
| **G-7** | MEDIUM | G-2, G-4 | Invoice query/download endpoints |
| **G-8** | LOW | G-1 | Auto-trigger global invoice on cash register close |
| **G-9** | LOW | — | FacturapiSettings configuration in appsettings |

---

## 4. Implementation Plan

### Step 1: Foundation (No external API calls)

| Task | Files | Gap |
|---|---|---|
| Create `Invoice` and `InvoiceOrder` entities | `POS.Domain/Models/Invoice.cs`, `InvoiceOrder.cs` | G-2 |
| Add fiscal fields to `OrderItem` | `POS.Domain/Models/OrderItem.cs` | G-3 |
| Create `SatPaymentForm` helper | `POS.Domain/Helpers/SatPaymentForm.cs` | G-5 |
| Create `FacturapiWebhookInbox` entity | `POS.Domain/Models/FacturapiWebhookInbox.cs` | G-4 |
| Add repositories for Invoice, FacturapiWebhookInbox | `POS.Repository/IRepository/`, `Repository/` | G-2, G-4 |
| Update UnitOfWork | `POS.Repository/IUnitOfWork.cs`, `UnitOfWork.cs` | G-2, G-4 |
| EF migration | `POS.Repository/Migrations/` | G-2, G-3, G-4 |
| Configure FacturapiSettings in appsettings | `appsettings.json`, `Program.cs` | G-9 |

### Step 2: Facturapi HTTP Integration

| Task | Files | Gap |
|---|---|---|
| Create `IFacturapiClient` interface + `FacturapiClient` (HttpClient wrapper) | `POS.Services/Adapter/IFacturapiClient.cs`, `FacturapiClient.cs` | G-1 |
| Methods: `CreateInvoiceAsync`, `CreateGlobalInvoiceAsync`, `CreateCustomerAsync`, `CancelInvoiceAsync`, `GetInvoiceAsync` | Same | G-1, G-6 |
| Register HttpClient in DI with `FacturapiSettings.ApiKey` header | `POS.Services/Dependencies/ServiceDependencies.cs` | G-1 |

### Step 3: Service Refactor

| Task | Files | Gap |
|---|---|---|
| Refactor `InvoicingService.CreateGlobalInvoiceAsync` — replace mock with `IFacturapiClient` call, create `Invoice` + `InvoiceOrder` records | `POS.Services/Service/InvoicingService.cs` | G-1, G-2 |
| Refactor `RequestIndividualInvoiceAsync` — same pattern | Same | G-1, G-2 |
| Build CFDI line items from `OrderItem` fiscal fields + `SatPaymentForm` mapping | Same | G-3, G-5 |
| Add `CancelInvoiceAsync` method | `IInvoicingService.cs`, `InvoicingService.cs` | G-6 |
| Add invoice query methods (`GetByIdAsync`, `GetByOrderAsync`, `DownloadAsync`) | Same | G-7 |

### Step 4: Webhook Processing

| Task | Files | Gap |
|---|---|---|
| Create `FacturapiWebhookController` (AllowAnonymous, validate signature) | `POS.API/Controllers/FacturapiWebhookController.cs` | G-4 |
| Create `FacturapiWebhookProcessorWorker` (BackgroundService, 10s poll) | `POS.API/Workers/FacturapiWebhookProcessorWorker.cs` | G-4 |
| Handle `invoice.status_updated`: update `Invoice.Status` + `Order.InvoiceStatus` + `InvoiceUrl` | `InvoicingService.cs` | G-4 |

### Step 5: Controller Endpoints

| Task | Files | Gap |
|---|---|---|
| Add `POST /api/invoicing/{id}/cancel` | `InvoicingController.cs` | G-6 |
| Add `GET /api/invoicing/{id}`, `GET /api/invoicing/{id}/download/{format}` | `InvoicingController.cs` | G-7 |
| Add `GET /api/invoicing/by-order/{orderId}` | `InvoicingController.cs` | G-7 |

### Step 6: Optional Automation

| Task | Files | Gap |
|---|---|---|
| Add global invoice trigger to cash register close (opt-in flag) | `CashRegisterService.cs` | G-8 |

---

## 5. What's NOT Needed (Already Solid)

These components are production-ready and need no changes:

- `FiscalCustomer` entity and repository — complete with RFC, regime, Facturapi ID, CRM link
- `PublicInvoicingController` — rate-limited, receipt proof validated, fully wired
- `InvoicingController` — endpoints exist, just need service methods to become real
- `Business` fiscal config fields — RFC, TaxRegime, LegalName, InvoicingEnabled, FacturapiOrganizationId all present
- `Product` SAT fields — SatProductCode, SatUnitCode, TaxRate all present
- `Order` invoice fields — InvoiceStatus, FacturapiId, InvoiceUrl, InvoicedAt, FiscalCustomerId all present
- `InvoiceStatus` enum — None, Pending, Issued, Cancelled
- `FacturapiSettings` config class — ApiKey, WebhookSecret, IsSandbox
- Receipt proof security model — TotalCents validation prevents unauthorized invoicing
