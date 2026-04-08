# AUDIT-021 — Schema Anti-Pattern Sweep

**Date:** 2026-04-08
**Scope:** Full scan of all 45 entities + 12 catalogs in `POS.Domain/Models/` for referential integrity violations.
**Audited by:** Automated deep scan of every property declaration, enum definition, and EF Core fluent configuration.

---

## Verdict: 27 Anti-Patterns Found

The schema contains **16 raw-string status/type columns** without catalog-backed FK integrity, **10 enums stored as strings** with no catalog FK, and **1 string-based FK** that should use an integer PK.

---

## Category A — Raw String Columns (No Enum, No FK, No Constraint)

These columns store free-text classification values with ZERO database-level enforcement. Any typo or invalid value will be silently accepted.

| # | Entity | Property | Current Type | Known Values | Severity |
|---|--------|----------|-------------|-------------|----------|
| A1 | `OrderPayment` | `Status` | `string` | "completed", "pending", "failed", "refunded" | **CRITICAL** — payment state with no constraint |
| A2 | `CashRegisterSession` | `Status` | `string` | "Open", "Closed" (uses `CashRegisterStatus` constants) | **CRITICAL** — financial session state |
| A3 | `CashMovement` | `Type` | `string` | "in", "out" | **HIGH** — financial movement classification |
| A4 | `RestaurantTable` | `Status` | `string` | "available", "occupied" | **MEDIUM** — operational state |
| A5 | `InventoryMovement` | `Type` | `string` | "in", "out", "adjustment" (legacy, duplicates `TransactionType` enum) | **HIGH** — redundant with enum |
| A6 | `InventoryItem` | `Unit` | `string` | free-text (legacy, duplicates `UnitOfMeasure` enum) | **MEDIUM** — redundant with enum |
| A7 | `Subscription` | `PlanType` | `string` | "Free", "Basic", "Pro", "Enterprise" | **HIGH** — billing state, PlanTypeCatalog exists but no FK |
| A8 | `Subscription` | `BillingCycle` | `string` | "Monthly", "Annual" | **MEDIUM** — no catalog or enum |
| A9 | `Subscription` | `PricingGroup` | `string` | "General", "Standard", "Restaurant" | **MEDIUM** — no catalog or enum |
| A10 | `Subscription` | `Status` | `string` | "active", "trialing", "past_due", "canceled", "paused" | **HIGH** — subscription lifecycle, Stripe-driven |
| A11 | `Device` | `Mode` | `string` | DeviceModeCatalog codes, but no FK link | **HIGH** — catalog exists but unused relationally |
| A12 | `DeviceActivationCode` | `Mode` | `string` | Same device modes, no FK | **HIGH** — same issue as Device.Mode |
| A13 | `BranchPaymentConfig` | `Provider` | `string` | "mercadopago", "clip" | **MEDIUM** — payment provider with no catalog |
| A14 | `PaymentWebhookInbox` | `Provider` | `string` | "MercadoPago", "Clip" | **MEDIUM** — same provider concept, inconsistent casing |
| A15 | `PaymentWebhookInbox` | `Status` | `string` | "pending", "failed" | **MEDIUM** — webhook processing state |
| A16 | `DiscountPreset` | `Type` | `string` | "percentage", "fixed" | **MEDIUM** — discount classification |

---

## Category B — Enums Stored as Strings Without Catalog FK

These properties use a C# enum with `HasConversion<string>()` in EF Core, providing compile-time safety in code but **ZERO database-level referential integrity**. Any direct SQL insert/update can inject invalid values. A corresponding Catalog table exists for some but is not linked via FK.

| # | Entity | Property | Enum Type | Catalog Exists? | FK Exists? | Severity |
|---|--------|----------|-----------|-----------------|-----------|----------|
| B1 | `Business` | `BusinessType` | `BusinessType` | Yes (`BusinessTypeCatalog`) | No (linked indirectly via `BusinessGiro`) | **LOW** — legacy, being replaced by multi-giro |
| B2 | `Business` | `PlanType` | `PlanType` | Yes (`PlanTypeCatalog`) | No | **HIGH** — catalog exists but no FK constraint |
| B3 | `User` | `Role` | `UserRole` | Yes (`UserRoleCatalog`) | No | **HIGH** — security-critical, catalog exists but no FK |
| B4 | `Order` | `SyncStatus` | `OrderSyncStatus` | Yes (`OrderSyncStatusCatalog`) | No | **MEDIUM** — catalog exists but no FK |
| B5 | `Order` | `KitchenStatus` | `KitchenStatus` | Yes (`KitchenStatusCatalog`) | No | **MEDIUM** — catalog exists but no FK |
| B6 | `Order` | `OrderSource` | `OrderSource` | No | No | **LOW** — no catalog yet |
| B7 | `Order` | `InvoiceStatus` | `InvoiceStatus` | No | No | **LOW** — no catalog yet |
| B8 | `Zone` | `Type` | `ZoneType` | Yes (`ZoneTypeCatalog`) | No | **MEDIUM** — catalog exists but no FK |
| B9 | `Promotion` | `Type` | `PromotionType` | Yes (`PromotionTypeCatalog`) | No | **MEDIUM** — catalog exists but no FK |
| B10 | `Promotion` | `AppliesTo` | `PromotionScope` | Yes (`PromotionScopeCatalog`) | No | **MEDIUM** — catalog exists but no FK |

**Note:** `PrintJob.Destination` and `PrintJob.Status` use `HasConversion<int>()` (stored as integers), which is slightly better but still has no FK constraint.

---

## Category C — String-Based Foreign Key (Non-Integer)

| # | Entity | Property | FK Target | Severity |
|---|--------|----------|-----------|----------|
| C1 | `BusinessGiro` | `CatalogCode` | `BusinessTypeCatalog.Code` (string) | **MEDIUM** — functional but violates integer FK mandate from Tech Lead |

---

## Category D — Clean Entities (No Issues Found)

The following entities have no anti-patterns — all classification fields use proper FKs or are genuinely free-text data fields:

- `Branch` — FKs are all int, no status strings
- `Category` — clean
- `Product` — SAT codes are external reference data, not internal state
- `ProductSize`, `ProductExtra`, `ProductImage`, `ProductConsumption` — clean
- `ProductTax`, `OrderItemTax`, `Tax` — clean relational
- `UserBranch` — clean junction table
- `Supplier`, `StockReceipt`, `StockReceiptItem` — clean
- `Customer` — clean
- `PushSubscription` — clean
- `Reservation` — uses `ReservationStatus` enum (no catalog yet, but lower priority)
- `Invoice` — uses proper enums, SAT codes are external standard
- `FiscalCustomer` — SAT fields are external fiscal codes, not internal classification

---

## Category E — Enums Without Catalogs (Future Consideration)

These enums have no matching catalog table. They are lower priority since they represent either external standards or less critical operational states:

| Enum | Used In | Priority |
|------|---------|----------|
| `OrderSource` | `Order`, `BranchDeliveryConfig` | Low — delivery platforms are external |
| `InvoiceStatus` | `Order`, `Invoice` | Low — small fixed set |
| `InvoiceType` | `Invoice` | Low — 2 values only |
| `DeliveryStatus` | `Order` | Low — external platform lifecycle |
| `ReservationStatus` | `Reservation` | Low — stable set |
| `CustomerTransactionType` | `CustomerTransaction` | Low — internal CRM |
| `UnitOfMeasure` | `InventoryItem` | Low — scientific units |
| `PrintingDestination` | `Product`, `PrintJob` | Low — hardware routing |
| `PrintJobStatus` | `PrintJob` | Low — stored as int already |
| `StripeEventStatus` | `StripeEventInbox` | Low — stored as string but Stripe-owned lifecycle |

---

## Severity Summary

| Severity | Count | Description |
|----------|-------|-------------|
| **CRITICAL** | 2 | Payment status and cash session status — financial integrity at risk |
| **HIGH** | 7 | Catalogs exist but have no FK link; or financial/security columns without constraint |
| **MEDIUM** | 12 | Operational state columns that should have enums or catalog FKs |
| **LOW** | 6 | Legacy fields being replaced, or external standard codes |
| **Total** | **27** | |

---

## Top Priority Remediation (Recommended Order)

### Tier 1 — CRITICAL (Fix Immediately)

| Finding | Current | Target |
|---------|---------|--------|
| A1: `OrderPayment.Status` | raw string | Create `PaymentStatusCatalog` + int FK |
| A2: `CashRegisterSession.Status` | raw string constant | Create `CashSessionStatusCatalog` + int FK |

### Tier 2 — HIGH (Fix Before Client Onboarding)

| Finding | Current | Target |
|---------|---------|--------|
| A7: `Subscription.PlanType` | raw string | FK to existing `PlanTypeCatalog.Id` |
| A10: `Subscription.Status` | raw string | Create `SubscriptionStatusCatalog` + int FK |
| A11+A12: `Device.Mode` + `DeviceActivationCode.Mode` | raw string | FK to existing `DeviceModeCatalog.Id` |
| B2: `Business.PlanType` | enum as string | FK to existing `PlanTypeCatalog.Id` |
| B3: `User.Role` | enum as string | FK to existing `UserRoleCatalog.Id` |
| A3: `CashMovement.Type` | raw string | Create `CashMovementTypeCatalog` + int FK |

### Tier 3 — MEDIUM (Next Sprint)

| Finding | Current | Target |
|---------|---------|--------|
| A4: `RestaurantTable.Status` | raw string | Create `TableStatusCatalog` + int FK |
| A5+A6: `InventoryMovement.Type` + `InventoryItem.Unit` | legacy strings | Remove after confirming enum migration complete |
| A8+A9: `Subscription.BillingCycle` + `PricingGroup` | raw strings | Create catalogs or enums |
| A13+A14: Payment Provider | raw strings | Create `PaymentProviderCatalog` + int FK |
| A15: `PaymentWebhookInbox.Status` | raw string | Create `WebhookStatusCatalog` or share with existing |
| A16: `DiscountPreset.Type` | raw string | Create `DiscountTypeCatalog` + int FK |
| B4-B5: `Order.SyncStatus/KitchenStatus` | enum no FK | Add int FK to existing catalogs |
| B8-B10: `Zone.Type`, `Promotion.Type/AppliesTo` | enum no FK | Add int FK to existing catalogs |
| C1: `BusinessGiro.CatalogCode` | string FK | Migrate to `BusinessTypeCatalogId` (int FK) |

---

## Architectural Note: AuditLog.Action

Finding `AuditLog.Action` (string) is intentionally **excluded from remediation**. Audit logs are append-only and must accept any action string, including future actions not yet defined. Constraining them to a catalog would break the audit trail's purpose. The `EntityType` field is similarly free-text by design.

---

## Appendix: Existing Catalogs vs Actual FK Usage

| Catalog Table | Entries | Has Entity FK? | Entities That Should Link |
|---------------|---------|---------------|--------------------------|
| `PlanTypeCatalog` | 4 | No | `Business.PlanType`, `Subscription.PlanType` |
| `BusinessTypeCatalog` | 12 | Partial (via `BusinessGiro.CatalogCode` string) | `BusinessGiro` should use int FK |
| `UserRoleCatalog` | 7 | No | `User.Role` |
| `PaymentMethodCatalog` | 4 | No | `OrderPayment.Method` |
| `KitchenStatusCatalog` | 3 | No | `Order.KitchenStatus` |
| `DisplayStatusCatalog` | 6 | No | (UI-only, used by frontend) |
| `ZoneTypeCatalog` | 3 | No | `Zone.Type` |
| `DeviceModeCatalog` | 4 | No | `Device.Mode`, `DeviceActivationCode.Mode` |
| `PromotionTypeCatalog` | 6 | No | `Promotion.Type` |
| `PromotionScopeCatalog` | 3 | No | `Promotion.AppliesTo` |
| `OrderSyncStatusCatalog` | 3 | No | `Order.SyncStatus` |
| `OnboardingStatusCatalog` | 4 | **Yes** (`Business.OnboardingStatusId`) | `Business` (done) |

**Score: 1 of 12 catalogs is properly linked via integer FK.**
