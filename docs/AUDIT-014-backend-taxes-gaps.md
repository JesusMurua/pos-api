# AUDIT-014: Tax (IVA) & Fiscal Compliance — Gap Analysis

**Date:** 2026-04-07
**Scope:** POS.API backend — Product, Order, OrderItem, Invoice entities, Facturapi integration, Stripe, sync DTOs
**Status:** Draft

---

## 1. Current State

### 1.1 Product Tax Fields

| Field | Type | Location | Notes |
|-------|------|----------|-------|
| `TaxRate` | `decimal?` | `Product.cs:53` | 0.16, 0.08, 0 — null defaults to 16% |
| `SatProductCode` | `string?` [10] | `Product.cs:44` | SAT catalog key (e.g., "90101500") |
| `SatUnitCode` | `string?` [5] | `Product.cs:50` | SAT unit key (e.g., "H87" = Pieza) |

**Missing:** No `IsTaxIncluded` boolean. The system **assumes** all prices include IVA. This is hard-coded in the tax formula and the Facturapi payload (`tax_included: true`).

### 1.2 OrderItem Tax Fields (Frozen at Sale Time)

| Field | Type | Location | Notes |
|-------|------|----------|-------|
| `TaxRatePercent` | `decimal?` | `OrderItem.cs:49` | Frozen from `Product.TaxRate` |
| `TaxAmountCents` | `int` | `OrderItem.cs:52` | Calculated: `floor(lineTotal * rate / (1 + rate))` |
| `SatProductCode` | `string?` [10] | `OrderItem.cs:42` | Frozen from product |
| `SatUnitCode` | `string?` [5] | `OrderItem.cs:46` | Frozen from product |

**Calculation formula** (`OrderService.cs:167`):
```csharp
var lineTotal = item.UnitPriceCents * item.Quantity;
item.TaxAmountCents = (int)(lineTotal * taxRate / (1 + taxRate));
```
This correctly reverses IVA from inclusive prices. Example: $116 MXN inclusive → Tax = $16, Base = $100.

### 1.3 Order Entity — Tax Fields

| Field | Type | Location | Notes |
|-------|------|----------|-------|
| `SubtotalCents` | `int` | `Order.cs:30` | Sum of `UnitPriceCents * Quantity` per item |
| `TotalCents` | `int` | `Order.cs` | `SubtotalCents - OrderDiscountCents` |
| `InvoiceStatus` | `InvoiceStatus` | `Order.cs:88` | None, Pending, Issued, Cancelled |
| `FiscalCustomerId` | `int?` | `Order.cs:102` | FK for individual CFDI |

**Missing:** No `TaxAmountCents` at order level. Tax is only calculated per item and then summed at invoice-creation time.

### 1.4 Invoice Entity

| Field | Type | Location |
|-------|------|----------|
| `TotalCents` | `int` | `Invoice.cs:42` |
| `SubtotalCents` | `int` | `Invoice.cs:45` |
| `TaxCents` | `int` | `Invoice.cs:48` |
| `PaymentForm` | `string` [2] | `Invoice.cs:56` — SAT payment form code |
| `PaymentMethod` | `string` [3] | `Invoice.cs:60` — Always "PUE" |
| `Currency` | `string` [3] | `Invoice.cs:52` — Hard-coded "MXN" |
| `Series` / `FolioNumber` | `string` | `Invoice.cs:35-39` |

The `Invoice` entity is the most complete fiscal record. It correctly stores the tax breakdown at the aggregate level.

### 1.5 Business Fiscal Configuration

| Field | Type | Location |
|-------|------|----------|
| `Rfc` | `string?` [13] | `Business.cs:32` |
| `TaxRegime` | `string?` [3] | `Business.cs:36` |
| `LegalName` | `string?` [300] | `Business.cs:40` |
| `InvoicingEnabled` | `bool` | `Business.cs:43` |
| `FacturapiOrganizationId` | `string?` [50] | `Business.cs:47` |

### 1.6 Facturapi Integration

- **Client:** `FacturapiClient.cs` — HTTP adapter for Facturapi REST API.
- **Tax payload per item:**
  ```json
  { "type": "IVA", "rate": 0.16, "tax_included": true }
  ```
- **Global invoices:** RFC `XAXX010101000`, regime `616`, customer "PUBLICO EN GENERAL".
- **Individual invoices:** Uses `FiscalCustomer` RFC, regime, and zip code.
- **SAT payment form mapping:** `SatPaymentForm.cs` — maps `PaymentMethod` enum → SAT code ("01" cash, "04" card, etc.).

### 1.7 Stripe Integration

- `StripeService.cs` handles SaaS subscription billing.
- **Zero tax logic.** Stripe Prices are created/managed externally. No `tax_behavior`, no `tax_code`, no automatic tax calculation.
- Stripe is for SaaS subscriptions only (not POS sales), so the impact is limited to whether Stripe invoices to the business need Mexican IVA.

### 1.8 Order Sync DTO

`SyncOrderRequest` and `SyncOrderItemRequest` — the offline-first sync payloads:

| DTO | Tax fields included? |
|-----|---------------------|
| `SyncOrderRequest` | `SubtotalCents` only — no tax fields |
| `SyncOrderItemRequest` | **None** — no `TaxRatePercent`, no `TaxAmountCents`, no SAT codes |
| `SyncPaymentRequest` | None |

Tax is populated server-side during sync by looking up `Product.TaxRate` at sync time (`OrderService.cs:158-167`).

---

## 2. Gaps

### GAP-1: No `IsTaxIncluded` Flag on Product

**Problem:** The entire system assumes prices include IVA (`tax_included: true` hard-coded in Facturapi). There is no per-product or per-business flag to indicate whether a price is tax-inclusive or tax-exclusive.

**Impact:**
- Cannot onboard businesses that price products without IVA (e.g., wholesale, export).
- The tax formula in `OrderService` only works for inclusive pricing. If a product has exclusive pricing, the formula produces wrong results.
- Facturapi receives `tax_included: true` for every item regardless.

### GAP-2: No `TaxAmountCents` on Order Entity

**Problem:** The `Order` entity has `SubtotalCents` and `TotalCents` but no `TaxAmountCents`. Tax is only stored per `OrderItem` and summed on-the-fly during invoice creation.

**Impact:**
- Dashboard and reports cannot show tax totals without iterating all items.
- The `RecalculateTotals()` method (`OrderService.cs:1302-1311`) does not compute or persist tax.
- Frontend cannot display a tax summary without a separate calculation.
- Accounting reconciliation requires loading all order items to derive tax.

### GAP-3: Sync DTO Discards Frontend Tax Calculations

**Problem:** `SyncOrderItemRequest` has no tax fields. The frontend may calculate tax client-side for display, but it is thrown away on sync. The backend recalculates using `Product.TaxRate` at sync time.

**Impact:**
- **Race condition:** If `Product.TaxRate` changes between when the order was created offline and when it syncs, the frozen rate will reflect the new rate, not the rate at time of sale. The receipt printed offline and the backend record will disagree.
- The frontend shows one tax amount; the backend stores a different one.

### GAP-4: Discount Tax Interaction Is Undefined

**Problem:** `Order.OrderDiscountCents` reduces `TotalCents`, but there is no corresponding adjustment to `TaxAmountCents`. When a discount is applied:
- Is the discount pre-tax (reduces base, then tax is recalculated)?
- Is the discount post-tax (reduces total, tax stays the same)?
- SAT requires discounts to be reflected in the CFDI "Descuento" node per item.

**Impact:**
- CFDI may have incorrect tax breakdown when discounts are applied.
- `InvoicingService` sums `TaxAmountCents` from items but items don't account for order-level discounts.
- SAT auditors can flag invoices where `Base - Descuento + IVA ≠ Total`.

### GAP-5: Hard-Coded Default Tax Rate

**Problem:** `0.16m` is hard-coded in two places:
- `OrderService.cs:163` — `product.TaxRate ?? 0.16m`
- `InvoicingService.cs:17` — `DefaultTaxRate = 0.16m`

**Impact:**
- Border-zone businesses (IVA 8%) must set `TaxRate` on every single product or they get 16%.
- If Mexico changes the IVA rate, a code deployment is required.
- No business-level default rate configuration.

### GAP-6: No Tax Exemption Validation

**Problem:** `Product.TaxRate` accepts any decimal. There is no validation that the rate is a SAT-valid value (0%, 8%, 16%). A product could be saved with `TaxRate = 0.05` and the system would happily calculate and invoice with it.

**Impact:**
- Facturapi may reject the invoice if the rate is not SAT-compliant.
- Silent data corruption — wrong tax amounts frozen into `OrderItem.TaxAmountCents`.

### GAP-7: StoreCredit / LoyaltyPoints SAT Ambiguity

**Problem:** `StoreCredit` and `LoyaltyPoints` payment methods map to SAT code `"99"` (Por definir). SAT does not accept `"99"` as a valid payment form for regular invoices.

**Impact:**
- Orders paid entirely with store credit cannot be invoiced via CFDI.
- Mixed-payment orders (cash + store credit) may have incorrect `FormaPago` if store credit is the dominant amount.

### GAP-8: Stripe SaaS Invoices Have No Mexican Tax Handling

**Problem:** `StripeService.cs` creates Prices and Subscriptions without `tax_behavior` or `automatic_tax`. Stripe invoices to Mexican businesses should include IVA.

**Impact:**
- Business owners receive Stripe invoices without IVA breakdown.
- Cannot deduct subscription costs for tax purposes without a proper CFDI or Stripe tax invoice.
- Low priority for POS sales, but relevant for B2B SaaS billing compliance.

### GAP-9: No CFDI "Descuento" Node per Item

**Problem:** The Facturapi payload (`FacturapiInvoiceItem`) does not include a `discount` field. Order-level discounts are not distributed to line items in the CFDI.

**Impact:**
- SAT CFDI 4.0 requires the "Descuento" attribute per concepto (line item) when applicable.
- Invoices for discounted orders may fail Facturapi validation or produce incorrect XML.

### GAP-10: Integer Truncation in Tax Calculation

**Problem:** `TaxAmountCents = (int)(lineTotal * taxRate / (1 + taxRate))` uses `(int)` cast which truncates (floors). Over many items, accumulated rounding errors can cause the sum of item taxes to differ from the expected order-level tax by several cents.

**Impact:**
- SAT validates that `Sum(Impuestos por concepto) == Total de Impuestos` in the CFDI XML. Rounding mismatches cause timbrado rejection.
- Facturapi may auto-correct but could also reject if the discrepancy exceeds tolerance.

---

## 3. Implementation Plan

### Phase A: Schema Hardening (Entity + Migration)

**Step A.1 — Add `IsTaxIncluded` to Product**

```
Product.cs → bool IsTaxIncluded = true   // Default: Mexican standard
```

This allows future support for exclusive-price products without breaking existing data.

**Step A.2 — Add `TaxAmountCents` to Order**

```
Order.cs → int TaxAmountCents = 0
```

Populated during `RecalculateTotals()` as `Sum(item.TaxAmountCents)`.

**Step A.3 — Add `DefaultTaxRate` to Business**

```
Business.cs → decimal DefaultTaxRate = 0.16m
```

Replaces both hard-coded `0.16m` references. Border-zone businesses set this to `0.08m`.

**Step A.4 — Add discount fields to OrderItem**

```
OrderItem.cs → int DiscountCents = 0   // Pro-rated share of order discount
```

When order-level discount is applied, distribute proportionally across items so each item's tax can be recalculated on the discounted base.

**Step A.5 — Migration**

```bash
dotnet ef migrations add AddTaxSchemaHardening
```

### Phase B: Tax Calculation Engine

**Step B.1 — Create `TaxCalculator` helper**

```
POS.Domain/Helpers/TaxCalculator.cs
```

Centralize the tax formula in one place:
- `CalculateInclusiveTax(int amountCents, decimal rate)` → returns tax cents using banker's rounding (`Math.Round(..., MidpointRounding.AwayFromZero)`).
- `CalculateExclusiveTax(int baseCents, decimal rate)` → for future exclusive pricing.
- `ValidateTaxRate(decimal rate)` → whitelist: 0, 0.08, 0.16.

**Step B.2 — Refactor `OrderService.RecalculateTotals()`**

- Use `TaxCalculator` instead of inline formula.
- Populate `Order.TaxAmountCents = items.Sum(i => i.TaxAmountCents)`.
- If order has discount: distribute proportionally, recalculate tax per item on discounted amount.

**Step B.3 — Refactor `InvoicingService.BuildInvoiceItems()`**

- Use `TaxCalculator` for consistency.
- Read `Business.DefaultTaxRate` instead of hard-coded constant.

### Phase C: Sync DTO Fix

**Step C.1 — Add tax fields to `SyncOrderItemRequest`**

```
SyncOrderItemRequest.cs →
    decimal? TaxRatePercent    // Frozen at client-side sale time
    int? TaxAmountCents        // Client-calculated tax
```

**Step C.2 — Update `OrderService.SyncOrdersAsync()`**

- If sync request includes `TaxRatePercent`, use it (trust the client snapshot).
- If null (legacy clients), fall back to `Product.TaxRate` lookup (current behavior).
- Always recalculate `TaxAmountCents` server-side from the frozen rate to prevent client tampering, but **use the client-provided rate** as the frozen rate.

### Phase D: CFDI Discount Compliance

**Step D.1 — Add `discount` to `FacturapiInvoiceItem`**

When building the Facturapi payload, include the per-item discount:

```json
{
  "product": { ... },
  "quantity": 2,
  "discount": 5.00   // Pro-rated discount in pesos
}
```

**Step D.2 — Update `InvoicingService` global/individual invoice builders**

- Distribute `Order.OrderDiscountCents` proportionally across items.
- Pass discount per item to Facturapi.
- Recalculate `SubtotalCents` and `TaxCents` accounting for discounts.

### Phase E: Validation & Configuration

**Step E.1 — Tax rate validation on Product create/update**

In `ProductService`, validate that `TaxRate` is one of: `0`, `0.08`, `0.16` (or null for business default).

**Step E.2 — SAT code validation**

Validate `SatProductCode` format (8 digits) and `SatUnitCode` format (2-3 alphanumeric) when `Business.InvoicingEnabled = true`.

**Step E.3 — Payment method fiscal rules**

- If dominant payment is `StoreCredit` or `LoyaltyPoints`, block CFDI generation or map to appropriate SAT code.
- Add validation in `InvoicingService` before calling Facturapi.

### Phase F: Stripe Tax (Low Priority)

**Step F.1 — Enable Stripe Tax for subscriptions**

- Configure `automatic_tax: { enabled: true }` on subscription creation.
- Set `tax_behavior: "inclusive"` on Stripe Prices.
- Store Stripe's tax breakdown in `Subscription` entity for reconciliation.

This phase is independent of POS sales and can be deferred until SaaS billing compliance is prioritized.

---

## 4. Summary Matrix

| Gap | Phase | Effort | Priority | Risk if Unresolved |
|-----|-------|--------|----------|-------------------|
| GAP-1: No `IsTaxIncluded` | A | Low | **Medium** | Blocks wholesale/export businesses |
| GAP-2: No order-level `TaxAmountCents` | A | Low | **Critical** | Reports and dashboards can't show tax |
| GAP-3: Sync DTO discards tax | C | Medium | **Critical** | Tax rate mismatch between offline receipt and backend |
| GAP-4: Discount-tax interaction | D | High | **Critical** | SAT rejects CFDI with wrong tax on discounted items |
| GAP-5: Hard-coded 16% | A | Low | **High** | Border-zone businesses get wrong default |
| GAP-6: No tax rate validation | E | Low | **High** | Invalid rates produce rejected CFDIs |
| GAP-7: StoreCredit SAT code | E | Low | **Medium** | Invoices with code "99" may be rejected |
| GAP-8: Stripe no tax | F | Medium | **Low** | SaaS invoices lack IVA — not POS-blocking |
| GAP-9: No CFDI discount node | D | Medium | **Critical** | SAT XML validation fails on discounted orders |
| GAP-10: Rounding truncation | B | Low | **High** | Timbrado rejection from penny-level mismatches |

---

## 5. Dependencies & Ordering

```
Phase A (schema) → Phase B (calculator) → Phase C (sync DTO)
                                        → Phase D (CFDI discounts)
                                        → Phase E (validation)
Phase F (Stripe) — independent, can run in parallel
```

Phase A must go first because B, C, D, and E all depend on the new fields. Phase F has no dependencies on the others.
