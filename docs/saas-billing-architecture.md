# SaaS Billing v2 — Architecture & Design (multi-rail, admin-operable)

> **Status:** **PR-1a + PR-1b delivered** (catalog + gap-closers; Subscription
> extension + data-sensitive backfill; suite 152 → 168). All open questions resolved
> (2026-06-08). PR-2 next (subscription admin surface + remote-first reconcile +
> dynamic Stripe Prices; flips BillingMethodId/BaseAmountCents NOT NULL + assigns
> them in the worker). Source of truth for the SaaS-billing redesign (PR-1…PR-7 below).
> Written to the same rigor as
> [payment-method-catalog-architecture.md](payment-method-catalog-architecture.md).
> **Scope:** how Fino charges the *tenant* the SaaS subscription fee — multi-rail,
> with custom pricing, invoicing, payments, add-ons, an admin operating surface,
> a persistent audit trail, and lifecycle notifications.
>
> **This doc covers the money rails (tenant → Fino).** It is deliberately distinct
> from the other three payment subsystems already in the codebase — do not conflate:
> 1. **CFDI invoicing** (tenant → *their* end customer): `Invoice`, `InvoicingService`,
>    Facturapi-as-tenant. See `Invoice.cs`.
> 2. **POS order payments** (end customer → tenant): `OrderPayment`, Clip/MercadoPago.
> 3. **Device-limit enforcement & hardware monetization:**
>    [monetization-architecture.md](monetization-architecture.md) is the SSoT for
>    *how many* devices a plan/add-on grants. **This doc owns the *charging*; that
>    doc owns the *enforcement*.** They meet at the add-on model — see §8.

---

## 0. Audit corrections folded into this design

This design was audited against the live code before writing. The raw proposal had
five blocking issues, four model gaps and eight minor items; all are resolved below.
This table is the changelog from the original spec so reviewers can see the *why*.

| # | Issue | Resolution in this doc |
|---|---|---|
| **B1** | `Invoice`/`InvoiceItem` collide with the existing CFDI `Invoice` (same namespace) | Renamed → **`SubscriptionInvoice` / `SubscriptionInvoiceItem`** (§4.4/4.5). |
| **B2** | `PUT /plan-types/{id}` price edit is reverted by the boot reseed (the §8b footgun) | **Resolved (OQ-3): catalog price is admin-editable.** The reseed becomes **upsert-except-`MonthlyPrice`** (insert sets the default; update leaves price untouched) so admin price edits are durable while `Code`/`Name` stay code-owned. Per-tenant negotiated price is still `Subscription.BaseAmountCents`. |
| **B3** | Custom `BaseAmountCents` is unexpressible on the Stripe rail (fail-closed `PriceMap`) | **Resolved (OQ-2): dynamic Stripe Prices.** The backend creates a Stripe Price per subscription via API and stores the active id on `Subscription.StripePriceId`; the static `PriceMap` is demoted to a seed hint. Custom pricing works on **any** rail (§7). |
| **B4** | `Subscription.BillingMethodId` NOT NULL would violate FK on existing prod rows (the PR-A1 incident) | Migration **backfills every existing Subscription to the Stripe rail** before the FK goes NOT NULL (§12). |
| **B5** | Admin↔Stripe reconcile has no source-of-truth/ordering rule (re-introduces the bug it kills) | **Remote-first** rule: call Stripe → await 2xx → then persist + price-history; the webhook is idempotent and reconciles via `UpdatedAt` without clobbering (§5). |
| **B6** | New `PlanAddOn`/`SubscriptionAddOn` duplicates the existing Stripe `SubscriptionItem` add-on model that feeds device-licensing | **Resolved (OQ-5): retire `SubscriptionItem`.** `SubscriptionAddOn` is the single SSoT; `DeviceService.EnforceDeviceLimitsAsync` and the Stripe webhook are rewired to it; the base-plan `StripeItemId` moves to `Subscription.StripeBaseItemId` (§8). PR-4 carries the data migration + test rewire. |
| **G1** | Notification "queue + retries" promised but no entity modeled | Added **`NotificationOutbox`** (§4.12, §10). |
| **G2** | Editable `NotificationTemplate` implied but not modeled; Welcome is hardcoded | v2 ships lifecycle templates **code-owned**; DB-editable templates deferred (§10, OQ-7). |
| **G3** | Tenant visibility of its own SaaS invoices undecided | v2 SaaS-billing tables are **operator-internal (not `IBusinessScoped`)**, `BusinessId` denormalized; tenant-facing billing screen is additive later (OQ-4). |
| **G4** | `PATCH /businesses/{id}/plan` (raw FK) left orphaned alongside the new flow | Old PATCH is **deprecated and routed through the new subscription service** (no raw FK write) (§9). |
| **M1** | Fiscal RFC duplicated between `Business` and `Subscription` | `Business` is the fiscal SSoT; the receptor CFDI fields are **frozen onto each `SubscriptionInvoice` at issue** (§4.4, §11). |
| **M2** | Spec reused `FeatureCacheGeneration` for the new catalog | Dedicated **`BillingMethodCacheGeneration`** + `ICatalogService.Invalidate("BillingMethods")`, matching the PR-B pattern. |
| **M3** | `InvoiceNumber` per-business sequence has a concurrency race | Per-business counter with row-lock (`Business.InvoiceCounter`), same discipline as `Branch.FolioCounter` (§4.4). |
| **M4** | Payment idempotency with nullable `Reference` | Partial unique index `(BillingMethodId, Reference) WHERE Reference IS NOT NULL`; idempotency is best-effort only when a reference exists (§4.7). |
| **M5** | Old Stripe-denormalized `PricingGroup`/`BillingCycle` overlap the new fields | Retained for the Stripe rail only; for manual rails they are null. `BaseAmountCents` is the SSoT of the charged amount regardless of rail (§4.3). |
| **M6** | PR-1 "foundation" is too large and is *not* schema-only (backfill) | Split into PR-1a/1b; the backfill migration is flagged data-sensitive (§12, §13). |
| **M7** | FK cycles cause multiple-cascade-path errors | Cross-referential FKs (`AppliedToInvoiceId`, `LinkedAddOnId`, …) use `ON DELETE RESTRICT`/`NO ACTION` (§4, §12). |
| **M8** | `PlanType.MonthlyPrice` is `decimal` pesos while everything else is `int` cents | All new money fields are **`*Cents int`**; the seed converts `MonthlyPrice × 100` when defaulting `BaseAmountCents`. |

---

## 1. Executive summary

Fino's SaaS fee is charged today only as **tenant self-service via Stripe Checkout**
— there is no way for the super-admin operator to activate a paid feature, raise a
tenant's charge, invoice it, register a payment received off-Stripe, or notify the
tenant. Fino positions as a "chameleon" product, and its own collection rails must
be **multi-rail**: Stripe (automatic), plus bank transfer, OXXO, cash, deposit,
cheque and "other" (manually registered by the operator when money arrives).

This redesign makes billing **admin-operable and rail-agnostic**: a data-driven
catalog of billing rails (mirroring `PaymentMethodCatalog`), an extended
`Subscription` with **negotiated per-tenant pricing** and an immutable price
history, first-class **`SubscriptionInvoice` / payments**, a unified add-on model,
a **reconcile rule** that ends the raw-FK-vs-Stripe tension, a persistent
**`BusinessAuditLog`**, and **lifecycle notifications** with retries. CFDI issued by
Fino itself (Fino as SaaS provider) is opt-in per subscription; the data model is
fully prepared but the Facturapi-as-issuer integration is deferred.

---

## 2. Context & trigger

### What exists and works
- Stripe subscription billing, base-plan complete: `Subscription` + `SubscriptionItem`,
  `StripeService`, `SubscriptionController` (tenant self-service checkout/cancel),
  `StripeWebhookController` (HMAC) → `StripeEventInbox` → `StripeEventProcessorWorker`
  (poll 5 s, out-of-order guard by `UpdatedAt`).
- `PlanTypeCatalog.MonthlyPrice` seeded (Free $0 / Basic $149 / Pro $349 / Enterprise null, MXN).
- Device-licensing reads add-ons (`SubscriptionItem`) via `DeviceService.EnforceDeviceLimitsAsync`.

### Documented tensions (from `backend-admin-surface-audit.md`)
- **No admin surface:** zero `/Admin/billing/*` or `/Admin/subscription/*` endpoints.
- **Raw-FK plan change:** `PATCH /Admin/businesses/{id}/plan` writes `Business.PlanTypeId`
  directly ([AdminBusinessesController.cs:411]) with **no Stripe reconciliation** — can
  diverge from what the tenant actually pays, or be overwritten by the next webhook.
- **Add-on placeholders:** `AddonPriceMap` is `price_dummy_*` ([StripeConstants.cs:164-169]).
- **Volatile audit:** admin actions go to Serilog only; no queryable `BusinessAuditLog`.
- **Single rail:** Stripe-only; the operator cannot record an off-Stripe payment.
- **Fire-and-forget email:** Welcome template only (Resend), no queue/retries.

### Why it's not optional
The real use case *"activate MercadoPago for vanidosademo → raise the monthly charge
→ invoice the add-on → notify"* is **✗ end-to-end** today — not for lack of billing,
but for lack of an **operating surface over it**, and because the single rail can't
represent money received by bank transfer or cash.

---

## 3. Architecture decision (summary)

1. **`SaaSBillingMethod`** — data-driven rail catalog (mirror of `PaymentMethodCatalog`):
   `IsSystem` policy, `IsAutomatic` (Stripe/OXXO yes; transfer/cash no), `RequiresReference`.
2. **`Subscription` extended** — `BillingMethodId`, `BaseAmountCents` (negotiated price,
   SSoT), `Currency`, `NextBillingDate`, `CfdiRequired`, billing contact + internal notes.
3. **`SubscriptionPriceHistory`** — append-only before/after/by/reason/effective.
4. **`SubscriptionInvoice` + `SubscriptionInvoiceItem`** — the SaaS invoice (renamed to
   avoid the CFDI `Invoice` collision); items typed Plan/AddOn/Discount/Adjustment.
5. **`TenantPayment`** — money received from the tenant, per rail, auto (webhook) or manual.
6. **`PlanAddOn` + `SubscriptionAddOn`** — unified add-on model (rail-agnostic), reconciled
   with the existing Stripe `SubscriptionItem` engine (§8).
7. **`BusinessAuditLog`** — persistent, admin-explicit (distinct from the transparent
   `AuditInterceptor`).
8. **`NotificationOutbox`** + lifecycle templates with retries.
9. **Reconcile rule** (remote-first) + **multi-rail manual flow**.
10. **`Business.SuspensionReason`** — closes the audit gap.

Money is **`int` cents** everywhere (M8). Auth reuses **`X-Admin-Token`**. Cache uses a
dedicated **`BillingMethodCacheGeneration`** (M2).

---

## 4. Data model

> Conventions: PKs are `int Id` identity unless noted. All money is `int …Cents`.
> Timestamps are UTC. "Scoped?" = whether the entity implements `IBusinessScoped`
> (tenant query filter). SaaS-billing tables are **operator-internal → not scoped**
> (G3); `BusinessId` is denormalized for indexing, not for tenant filtering.

### 4.1 `SaaSBillingMethod` — rail catalog (data-driven)

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | no | PK |
| Code | string(20) | no | Unique. Stable freeze key (e.g. `Stripe`, `BankTransfer`). |
| Name | string(60) | no | Display. |
| IsAutomatic | bool | no | true ⇒ rail confirms payment via webhook (Stripe, OxxoPay); false ⇒ operator registers manually. |
| RequiresReference | bool | no | true ⇒ a payment on this rail must carry a `Reference` (bank folio, txn id). |
| ProviderKey | string(30)? | yes | `stripe` / `conekta` / null. |
| CountryCode | string(2)? | yes | null = all countries. |
| SortOrder | int | no | |
| IsActive | bool | no | Soft-delete flag. |
| IsSystem | bool | no | Code-owned; reseed overwrites; admin edits not guaranteed durable (see §8b of the payment doc). |

**Seed (7 system rails):** `Stripe` (automatic, provider `stripe`), `BankTransfer`
(manual, requires reference), `OxxoPay` (automatic, provider — see OQ-6), `Cash`
(manual), `BankDeposit` (manual, requires reference), `Check` (manual, requires
reference), `Other` (manual). All `IsSystem=true`.

Cache invalidation via **`BillingMethodCacheGeneration`** + `ICatalogService.Invalidate("BillingMethods")` (M2).

### 4.2 `Business` — add columns

| Column | Type | Null | Notes |
|---|---|---|---|
| SuspensionReason | string(300)? | yes | Free text set on suspend; cleared on reactivate (M10/audit gap). |
| InvoiceCounter | int | no | Per-business monotonic counter for `SubscriptionInvoice.InvoiceNumber`; incremented under row-lock (M3). Default 0. |

### 4.3 `Subscription` — extend the existing entity

Existing kept: `BusinessId`, `StripeCustomerId`, `StripeSubscriptionId`, `Items`,
`PlanTypeId`, `BillingCycle`, `PricingGroup`, `Status`, `TrialEndsAt`,
`CurrentPeriodStart/End`, `CanceledAt`, `UpdatedAt`.

> **M5 — coexistence:** `BillingCycle`/`PricingGroup` are Stripe-derived and remain
> populated **only for the Stripe rail** (null for manual rails). `BaseAmountCents`
> is the SSoT of what the tenant is charged on **any** rail.

| New column | Type | Null | Notes |
|---|---|---|---|
| BillingMethodId | int (FK→SaaSBillingMethod) | no* | *NOT NULL **after** backfill (§12). `ON DELETE RESTRICT`. |
| BaseAmountCents | int | no | Negotiated current price (M8) — the **per-tenant SSoT** of what this tenant is charged. Seed default = `PlanType.MonthlyPrice × 100`. Editing the catalog price (OQ-3) does **not** retroactively change this. |
| Currency | string(3) | no | Default "MXN". |
| NextBillingDate | DateTime? | yes | Drives the invoice-generation job. |
| CfdiRequired | bool | no | Default false. true ⇒ each closed invoice should emit Fino's own CFDI (deferred, §11). |
| BillingEmail | string(150)? | yes | Where invoices/receipts go; falls back to owner email. |
| Notes | string(500)? | yes | Operator-internal. |
| StripePriceId | string(64)? | yes | **OQ-2:** the dynamic Stripe Price (created per subscription via API) that currently backs `BaseAmountCents` on the Stripe rail. Null for manual rails. The static `PriceMap` is only a seed hint now. |
| StripeBaseItemId | string(64)? | yes | **OQ-5:** the base-plan `si_…` item id (was on the retired `SubscriptionItem`). Lets the webhook target the base item for quantity/price updates. |

> **M1 — fiscal:** the *receptor* RFC/regime/use/postal-code are **not** stored here.
> `Business` is the fiscal SSoT; those fields are **frozen onto each
> `SubscriptionInvoice` at issue time** (§4.4) so historical invoices never drift.

### 4.4 `SubscriptionInvoice` — the SaaS invoice (renamed, B1)

> **Not** `POS.Domain.Models.Invoice` (that is the tenant's CFDI to its customer).

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | no | PK |
| SubscriptionId | int (FK) | no | `ON DELETE RESTRICT` |
| BusinessId | int (FK) | no | Denormalized (G3). Indexed. |
| InvoiceNumber | int | no | Per-business sequence from `Business.InvoiceCounter` (M3). Unique `(BusinessId, InvoiceNumber)`. |
| Status | enum string | no | `Open` / `Paid` / `PartiallyPaid` / `Overdue` / `Void` / `Refunded`. |
| IssuedAtUtc | DateTime | no | |
| DueDate | DateTime | no | |
| PeriodStart | DateTime | no | |
| PeriodEnd | DateTime | no | |
| SubtotalCents | int | no | |
| TaxCents | int | no | IVA — see OQ (Stripe Tax vs backend). |
| TotalCents | int | no | |
| Currency | string(3) | no | "MXN". |
| CreatedByTokenIdHash | string(64)? | yes | null ⇒ created by the generation job. |
| StripeInvoiceId | string(64)? | yes | Set when the Stripe rail produced it. |
| **CFDI (frozen receptor, all nullable until CfdiRequired + §11)** | | | |
| ReceptorRfc | string(13)? | yes | Frozen from `Business.Rfc` at issue. |
| ReceptorRegime | string(3)? | yes | Frozen `Business.TaxRegime`. |
| ReceptorLegalName | string(300)? | yes | Frozen `Business.LegalName`. |
| ReceptorPostalCode | string(5)? | yes | Receptor CP (CFDI 4.0). |
| CfdiUseCode | string(4)? | yes | e.g. `G03`. |
| SatPaymentFormCode | string(2)? | yes | Frozen from the paying rail. |
| SatUuid | string(40)? | yes | Folio fiscal once stamped. |
| SatStampedAt | DateTime? | yes | |
| SatXmlUrl / SatPdfUrl | string(500)? | yes | |

### 4.5 `SubscriptionInvoiceItem`

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | no | PK |
| InvoiceId | int (FK) | no | `ON DELETE CASCADE` (items die with their invoice). |
| Description | string(200) | no | |
| Quantity | int | no | |
| UnitAmountCents | int | no | |
| TotalAmountCents | int | no | Negative for `Discount`. |
| ItemType | enum string | no | `PlanBase` / `AddOn` / `Discount` / `Adjustment`. |
| LinkedAddOnId | int (FK→PlanAddOn)? | yes | `ON DELETE RESTRICT` (M7). |
| LinkedPlanTypeId | int (FK→PlanTypeCatalog)? | yes | `ON DELETE RESTRICT` (M7). |

### 4.6 `SubscriptionPriceHistory` — append-only

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | no | PK |
| SubscriptionId | int (FK) | no | `ON DELETE RESTRICT` |
| BeforeAmountCents | int | no | |
| AfterAmountCents | int | no | |
| ChangedAtUtc | DateTime | no | |
| ChangedByTokenIdHash | string(64) | no | |
| Reason | string(300) | no | Commercial reason (discount, negotiated, free month). |
| EffectiveDate | DateTime | no | |
| AppliedToInvoiceId | int (FK→SubscriptionInvoice)? | yes | `ON DELETE NO ACTION` (M7). |

Append-only: no update/delete path; not editable via any endpoint.

### 4.7 `TenantPayment` — money received from the tenant

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | no | PK |
| InvoiceId | int (FK→SubscriptionInvoice) | no | `ON DELETE RESTRICT` |
| BillingMethodId | int (FK→SaaSBillingMethod) | no | `ON DELETE RESTRICT` |
| AmountCents | int | no | |
| Currency | string(3) | no | |
| PaidAtUtc | DateTime | no | |
| Reference | string(120)? | yes | Bank folio / Stripe charge id. |
| Notes | string(300)? | yes | |
| ReceivedByTokenIdHash | string(64)? | yes | null ⇒ automatic (webhook); hash ⇒ manual entry. |
| StripeChargeId | string(64)? | yes | |
| RawWebhookPayloadJson | text? | yes | Audit of the source event. |

**Idempotency (M4):** partial unique index `(BillingMethodId, Reference) WHERE
Reference IS NOT NULL`. Rails without a reference (cash) cannot be deduped — the
operator owns correctness there (and `DELETE …/payments/{id}` fixes capture errors).

### 4.8 `PlanAddOn` — billable add-on catalog (data-driven)

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | no | PK |
| Code | string(30) | no | Unique. |
| Name | string(60) | no | |
| Description | string(300)? | yes | |
| BillingCycle | enum string | no | `OneTime` / `Monthly` / `Annual`. |
| DefaultPriceCents | int | no | |
| Currency | string(3) | no | "MXN". |
| LinkType | enum string | no | `PaymentMethod` / `Feature` / `BranchSlot` / `DeviceLicense` / `Custom`. |
| LinkedEntityId | int? | yes | Meaning depends on `LinkType` (e.g. a `FeatureKey` id for `DeviceLicense`). |
| StripePriceId | string(64)? | yes | The Stripe Price that materializes this add-on on the Stripe rail (§8). |
| IsActive | bool | no | |
| IsSystem | bool | no | |

**Seed:** the three current device-license dummies materialize here
(`DeviceLicense` linked to `MaxKdsScreens`/`MaxKiosks`/`MaxCashRegisters`) plus a
`PaymentMethod`-linked add-on example (e.g. "MercadoPago rail"). Real `StripePriceId`s
replace the dummies.

### 4.9 `SubscriptionAddOn` — active add-ons per subscription

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | no | PK |
| SubscriptionId | int (FK) | no | `ON DELETE CASCADE` |
| AddOnId | int (FK→PlanAddOn) | no | `ON DELETE RESTRICT` |
| Quantity | int | no | Default 1 (device licenses are quantitative). |
| ActivatedAt | DateTime | no | |
| DeactivatedAt | DateTime? | yes | Soft lifecycle; row kept for history. |
| CustomPriceCents | int? | yes | Overrides `PlanAddOn.DefaultPriceCents`. |
| ActivatedByTokenIdHash | string(64) | no | |
| Reason | string(300)? | yes | |
| StripeItemId | string(64)? | yes | Links to `SubscriptionItem.StripeItemId` on the Stripe rail (§8). |

### 4.10 `BusinessAuditLog` — persistent admin action log

Distinct from the transparent `AuditInterceptor` (which mirrors tenant entity CRUD).
This is **admin-explicit**: one row per operator action.

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | no | PK |
| BusinessId | int (FK) | no | Indexed. |
| Action | enum string | no | `Created` / `Suspended` / `Reactivated` / `PlanChanged` / `TrialExtended` / `PasswordReset` / `Impersonated` / `SubscriptionPriceChanged` / `AddOnActivated` / `AddOnDeactivated` / `InvoiceCreated` / `InvoiceVoided` / `PaymentRegistered` / `PaymentDeleted` / `CfdiToggled` / `NotificationSent` / `Other`. |
| BeforeJson | text? | yes | |
| AfterJson | text? | yes | |
| Reason | string(300)? | yes | |
| ChangedAtUtc | DateTime | no | |
| ChangedByTokenIdHash | string(64)? | yes | null ⇒ system/job. |

### 4.11 `NotificationOutbox` — durable queue (G1)

| Column | Type | Null | Notes |
|---|---|---|---|
| Id | int | no | PK |
| BusinessId | int (FK)? | yes | Recipient context (null for non-tenant mail). |
| TemplateCode | string(50) | no | e.g. `InvoiceCreated`. |
| ToEmail | string(150) | no | |
| PayloadJson | text | no | Template parameters. |
| Status | enum string | no | `Pending` / `Sent` / `Failed`. |
| Attempts | int | no | Default 0. |
| NextAttemptAtUtc | DateTime | no | Backoff schedule. |
| LastError | string(500)? | yes | |
| CreatedAtUtc / SentAtUtc | DateTime / DateTime? | | |

A `NotificationDispatchWorker` (BackgroundService, same pattern as
`StripeEventProcessorWorker`) drains `Pending`/due rows with exponential backoff.

---

## 5. Reconcile admin ↔ Stripe (B5 — the core correctness rule)

The redesign exists to kill the raw-FK-vs-Stripe drift. The rule is **remote-first,
webhook-idempotent**:

1. Admin calls `PUT /Admin/businesses/{id}/subscription` with the change set.
2. If `BillingMethod = Stripe` and a `StripeSubscriptionId` exists:
   a. Call the Stripe SDK to update the remote subscription (proration policy — OQ-1).
   b. **Await a 2xx.** Only then persist local changes (`Subscription`, +
      `SubscriptionPriceHistory`, + `SubscriptionAddOn`) in one DB transaction, and
      set `Subscription.UpdatedAt = now`.
   c. On any Stripe 4xx/5xx: **roll back the DB transaction** and surface the error
      to the operator (no silent partial state).
3. The confirming webhook `customer.subscription.updated` arrives later. The existing
   `StripeEventProcessorWorker` guard (compare event time vs `Subscription.UpdatedAt`)
   makes it **idempotent**: it reconciles fields but does not clobber the just-applied
   admin change. The webhook remains the source of truth for Stripe-side state
   (status, period, items); the admin write is consistent because it went through
   Stripe first.
4. If `BillingMethod ≠ Stripe` (manual rail): there is no remote call; persist locally
   and bill via `SubscriptionInvoice` (§6). The Stripe worker never touches these rows.

> **Deprecation (G4):** `PATCH /Admin/businesses/{id}/plan` stops doing a raw FK write.
> It is kept as a thin alias that calls the same subscription service (plan-only change),
> so the FK tension cannot return through the back door. New UI uses `PUT …/subscription`.

> **B3 constraint:** a Stripe-rail subscription can only carry a `BaseAmountCents` that
> corresponds to a **registered** Stripe Price (the worker's `PriceMap` is fail-closed
> — an unknown price throws, see [StripeConstants.cs:125-134]). Negotiated/custom amounts
> ⇒ move the tenant to a manual rail. Dynamic custom Stripe prices are OQ-2.

---

## 6. Multi-rail flow (manual rails)

For non-automatic rails (transfer, OXXO ticket, cash, deposit, cheque, other):

1. Operator (or the generation job) creates an invoice: `POST /Admin/businesses/{id}/invoices`
   with items. It opens `Status=Open`, `DueDate` set, `InvoiceNumber` from the
   per-business counter (M3).
2. When the money arrives, the operator records it:
   `POST /Admin/invoices/{id}/payments` with `{ billingMethodId, amountCents, reference?, paidAtUtc, notes? }`.
3. Status transitions: sum of `TenantPayment.AmountCents` ≥ `TotalCents` ⇒ `Paid`;
   `0 < sum < Total` ⇒ `PartiallyPaid`; past `DueDate` and unpaid ⇒ `Overdue` (set by job).
4. Idempotency: a repeated `(billingMethodId, reference)` (when reference present) maps
   to the same payment row (M4). Capture errors are fixed with `DELETE …/payments/{id}`.
5. Invoices are never hard-deleted — only `POST /Admin/invoices/{id}/void` (audited).

---

## 7. Custom pricing — dynamic Stripe Prices (B3 / OQ-2)

- **Per-tenant price SSoT = `Subscription.BaseAmountCents`.** `PlanType.MonthlyPrice`
  is the catalog default (now admin-editable, OQ-3) used only to seed new subscriptions;
  editing it does not retroactively change negotiated `BaseAmountCents`.
- **Manual rails:** `BaseAmountCents` is free-form; each cycle the job emits a
  `SubscriptionInvoice` whose `PlanBase` line = `BaseAmountCents`. Discounts/free
  months are negative `Discount` line items + a `SubscriptionPriceHistory` row.
- **Stripe rail (dynamic prices):** to charge a negotiated amount the backend **creates
  a Stripe Price via API** for that subscription and points the Stripe subscription item
  at it, storing the active id on `Subscription.StripePriceId`. Consequences:
  - The static `StripeConstants.PriceMap` is **demoted to a seed hint** — plan/cycle/
    group are no longer resolved from the price id. The webhook
    (`StripeEventProcessorWorker` / `ResolvePlanType`) is rewired to read the **local**
    `Subscription` mapping instead of the fail-closed `PriceMap` lookup.
  - **Orphan cleanup:** Stripe Prices are immutable and cannot be deleted — only
    **archived** (`active=false`). When `BaseAmountCents` changes, the old dynamic Price
    is archived via API (on-change; a sweeper job is the fallback).
  - This grows PR-2 (dynamic-price infra + worker rewire) — flagged in §13.

---

## 8. Add-on model reconciliation (B6 — meets monetization-architecture.md)

Two truths must agree: **billing** (this doc) and **device-limit enforcement**
([monetization-architecture.md](monetization-architecture.md), via
`DeviceService.EnforceDeviceLimitsAsync` which today sums `SubscriptionItem`
quantities — [DeviceService.cs:698]).

Decision (OQ-5) — **retire `SubscriptionItem`; `SubscriptionAddOn` is the single SSoT**:
- The `SubscriptionItem` entity, repository and table are **removed**. The base plan's
  Stripe item id moves to `Subscription.StripeBaseItemId` (§4.3); add-on items are
  represented solely by `SubscriptionAddOn` (with `StripeItemId` on the Stripe rail).
- **Stripe rail:** the webhook (`SyncItemsAndPlan`) is rewritten to distribute
  `stripe_subscription.items.data` → the base item onto `Subscription.StripeBaseItemId`,
  every other item onto a matching `SubscriptionAddOn` (by `StripeItemId`).
- **Manual rails:** no Stripe items; `SubscriptionAddOn` stands alone, billed via
  `SubscriptionInvoice` line items.
- **Device-licensing rewire:** `DeviceService.EnforceDeviceLimitsAsync` changes from
  "sum `SubscriptionItem` quantities" ([DeviceService.cs:698]) to "sum active
  `SubscriptionAddOn.Quantity` of `LinkType=DeviceLicense` for the relevant
  `FeatureKey`". This is the meeting point with
  [monetization-architecture.md](monetization-architecture.md).

> This is the single most invasive change. **PR-4** carries: the `SubscriptionItem`
> removal, a **data migration** (existing items → `Subscription.StripeBaseItemId` +
> `SubscriptionAddOn`), the webhook rewrite, the `DeviceService` rewire, and the
> device-licensing test rewrite. The `ISubscriptionItemRepository`/`UnitOfWork`
> members are dropped with it.

---

## 9. Admin endpoints (`X-Admin-Token`)

Pattern reuse: `AdminFeatureMatrixController` / `PaymentMatrixAdminController`
(scheme `AdminTokenAuthenticationHandler.SchemeName`, hashed `token_id` audit, bump +
`ICatalogService.Invalidate` on catalog mutations). All mutations also write
`BusinessAuditLog` where a business is in scope.

```
# Rails catalog
GET    /api/Admin/billing-methods
POST   /api/Admin/billing-methods
PUT    /api/Admin/billing-methods/{id}
DELETE /api/Admin/billing-methods/{id}        # soft if has payments; 409 if IsSystem

# Plan catalog (price admin-editable — OQ-3)
GET    /api/Admin/plan-types
PUT    /api/Admin/plan-types/{id}               # edit MonthlyPrice/Name; bump Invalidate("PlanTypes")

# Add-on catalog
GET    /api/Admin/plan-add-ons
POST   /api/Admin/plan-add-ons
PUT    /api/Admin/plan-add-ons/{id}
DELETE /api/Admin/plan-add-ons/{id}

# Subscription (reconcile, §5)
GET    /api/Admin/businesses/{id}/subscription                 # subscription + price history + add-ons
PUT    /api/Admin/businesses/{id}/subscription                 # plan/price/billing-method/cfdi-flag
POST   /api/Admin/businesses/{id}/subscription/add-ons         # activate
DELETE /api/Admin/businesses/{id}/subscription/add-ons/{addOnId}

# Invoicing & payments (§6)
GET    /api/Admin/businesses/{id}/invoices
POST   /api/Admin/businesses/{id}/invoices
GET    /api/Admin/invoices/{id}
PUT    /api/Admin/invoices/{id}                 # while Open only
POST   /api/Admin/invoices/{id}/void
POST   /api/Admin/invoices/{id}/payments
DELETE /api/Admin/invoices/{id}/payments/{paymentId}

# Audit
GET    /api/Admin/businesses/{id}/audit-log     # persistent timeline
GET    /api/Admin/audit-log                      # cross-tenant, paged/filterable

# Metrics
GET    /api/Admin/billing/metrics                # MRR, ARR, churn, revenue/month, retention
GET    /api/Admin/billing/upcoming-invoices

# Notifications
POST   /api/Admin/businesses/{id}/notify         # manual send via template
GET    /api/Admin/notification-templates         # read (edit deferred — OQ-7)

# Deprecated (now routes through the subscription service, no raw FK — G4)
PATCH  /api/Admin/businesses/{id}/plan
```

The wire contract (shapes, null handling, enum casing, status codes) is authored as a
companion `docs/saas-billing-api.md` at implementation time, mirroring
[payment-method-catalog-api.md](payment-method-catalog-api.md).

---

## 10. Notifications

- Keep `EmailService` + Resend; add the **`NotificationOutbox`** (§4.11) and a
  `NotificationDispatchWorker` for durable retries (fixes the fire-and-forget fragility).
- v2 templates are **code-owned** (G2); DB-editable templates deferred (OQ-7). The
  `GET /notification-templates` endpoint lists the code-owned set.

| Code | Trigger | Recipient | Key params |
|---|---|---|---|
| Welcome (exists) | business created (`SuppressWelcomeEmail=false`) | owner | name, businessName |
| InvoiceCreated | invoice opened | billing email | invoiceNumber, total, dueDate |
| PaymentReceived | `TenantPayment` recorded | billing email | amount, method, invoiceNumber |
| PaymentOverdue | invoice past due (job) | billing email | invoiceNumber, total, daysLate |
| PaymentFailed | Stripe `invoice.payment_failed` | billing email | invoiceNumber, retryDate |
| SubscriptionPriceChanged | price history row | owner | before, after, effectiveDate |
| PlanChanged | plan change | owner | oldPlan, newPlan |
| AddOnActivated | add-on activated | owner | addOnName, price |
| TrialExpiring3d / TrialExpiring1d | job vs `TrialEndsAt` | owner | daysLeft, plan |
| TrialExpired / TrialConverted | job / first paid invoice | owner | plan |
| Suspended / Reactivated | status change | owner | reason |
| CfdiIssued / CfdiCancelled | deferred with §11 | billing email | uuid, pdfUrl |

---

## 11. CFDI opt-in (Fino as issuer — deferred)

Product decision (closed): CFDI is **opt-in per subscription** (`CfdiRequired`). When
true, each closed `SubscriptionInvoice` should emit **Fino's own CFDI** (Fino as the
SaaS provider/emisor — a Facturapi org **separate** from the tenant's). The model is
fully prepared (frozen receptor fields on `SubscriptionInvoice`, §4.4); the
Facturapi-as-issuer integration is **PR-7, deferred**. No migration debt: enabling it
later only fills already-present nullable columns.

**IVA (OQ-8):** the **backend computes tax** (default 16% MX, configurable) and writes
`SubscriptionInvoice.TaxCents` per invoice — chosen over Stripe Tax to keep one tax
engine aligned with Fino's own CFDI (PR-7). Stripe Tax is a possible future upgrade.

Blockers for PR-7: Fino's own RFC/regime, Facturapi issuer onboarding.

---

## 12. Migration & backfill strategy

> **Not schema-only** (unlike PR-B). The `BillingMethodId` step mutates existing rows
> — treat with PR-A1 discipline (the FK-violation incident: a NOT NULL FK over
> non-empty prod data needs an in-migration backfill).

Order within the foundation migration(s):
1. Create `SaaSBillingMethod`; seed the 7 system rails (in `DbInitializer`, idempotent).
2. Add `Subscription.BillingMethodId` **nullable**, then `UPDATE Subscription SET
   BillingMethodId = (Stripe rail id)` for all existing rows, **then** alter to NOT NULL
   + FK `ON DELETE RESTRICT` (B4).
3. Add `Subscription.BaseAmountCents` etc.; backfill `BaseAmountCents =
   COALESCE(PlanType.MonthlyPrice,0) × 100` per existing subscription (M8).
4. Add `Business.SuspensionReason`, `Business.InvoiceCounter` (default 0).
5. Create the remaining tables (price history, invoice, invoice item, tenant payment,
   plan add-on, subscription add-on, business audit log, notification outbox). All
   cross-referential FKs `RESTRICT`/`NO ACTION` to avoid multiple-cascade-path errors (M7).

Seed runs in `DbInitializer` after migrate, idempotent, all envs (matches the
established pattern).

> **OQ-3 reseed change:** `UpsertPlanTypeCatalog` switches from "upsert all fields" to
> **upsert-except-`MonthlyPrice`** — on existing rows it still corrects `Code`/`Name`/
> `SortOrder`/`Currency` from code, but **never touches `MonthlyPrice`** (set only on
> insert). This makes admin price edits durable while keeping the rest code-owned, with
> no `IsManaged` flag needed. (Contrast with the system-method §8b footgun, which this
> deliberately avoids for price.)

---

## 13. Implementation plan (PRs)

Refined from the spec (PR-1 was too large and not schema-only — M6):

- **PR-1a (catalog + gap-closers):** `SaaSBillingMethod` + 7-rail seed,
  `Business.SuspensionReason` + `InvoiceCounter`, `BusinessAuditLog`, dedicated
  `BillingMethodCacheGeneration`, plan-types `GET/PUT` + the **reseed
  upsert-except-`MonthlyPrice`** change (OQ-3). Refactor existing admin actions
  (suspend/plan/trial/reset/impersonate/create) to also write `BusinessAuditLog`. No
  Stripe logic.
- **PR-1b (subscription extension + backfill):** `Subscription` new columns +
  `SubscriptionPriceHistory`, with the **data-sensitive backfill** (§12). No endpoints.
- **PR-2 (subscription admin surface + reconcile):** `GET/PUT …/subscription`,
  remote-first reconcile (§5), **dynamic Stripe Price infra** (create/archive per
  subscription, store `Subscription.StripePriceId`) + **worker rewire** off the static
  `PriceMap` (OQ-2), deprecate `PATCH …/plan` (route through the service).
- **PR-3 (invoicing + payments):** `SubscriptionInvoice`/`Item`/`TenantPayment` CRUD,
  manual payment flow (§6), Stripe webhook → auto `TenantPayment`, invoice-generation job.
- **PR-4 (add-ons + retire `SubscriptionItem`):** `PlanAddOn` + `SubscriptionAddOn`,
  activation flow; **remove `SubscriptionItem`** (entity/repo/table) with the data
  migration (existing items → `Subscription.StripeBaseItemId` + `SubscriptionAddOn`),
  the webhook `SyncItemsAndPlan` rewrite, the `DeviceService.EnforceDeviceLimitsAsync`
  rewire (§8) and its test rewrite; proration (OQ-1: Stripe `create_prorations` on the
  Stripe rail, `Adjustment` line item next invoice on manual rails).
- **PR-5 (notifications):** `NotificationOutbox` + dispatch worker + retries + the
  lifecycle templates (§10).
- **PR-6 (metrics):** MRR/ARR/churn/retention/upcoming-invoices endpoints.
- **PR-7 (CFDI issuer, deferred):** Facturapi-as-Fino, auto-emit on invoice close when
  `CfdiRequired`.

Dependencies: PR-2 needs PR-1b; PR-3 needs PR-1b; PR-4 needs PR-3 + touches
device-licensing; PR-6 needs PR-3 data; PR-7 needs PR-3.

---

## 14. Open questions — **all Resolved** (2026-06-08)

Closed by the product owner; folded into the sections above.

- **OQ-1 — Proration → Resolved: immediate pro-rata.** Stripe rail uses native
  `proration_behavior: 'create_prorations'`; manual rails apply an `Adjustment` line
  item on the next invoice. (§13 PR-4.)
- **OQ-2 — Custom pricing on Stripe → Resolved: dynamic Stripe Prices.** Backend creates
  a Price per subscription via API, maps the active id on `Subscription.StripePriceId`,
  archives orphans on change (sweeper fallback). Static `PriceMap` demoted to a seed
  hint; webhook reads the local mapping. (§4.3, §7, §13 PR-2.)
- **OQ-3 — Editable catalog plan price → Resolved: admin-editable.** `GET/PUT
  /Admin/plan-types` with `Invalidate("PlanTypes")`; reseed becomes
  **upsert-except-`MonthlyPrice`** (no `IsManaged` flag needed). (§9, §12, B2.)
- **OQ-4 — Tenant visibility → Resolved: post-v2.** Tables keep `BusinessId`
  denormalized; fino-app adds scoped read endpoints when it gets there. (§4, G3.)
- **OQ-5 — Add-on SSoT → Resolved: retire `SubscriptionItem`.** `SubscriptionAddOn` is
  the only SSoT; device-licensing + webhook rewired; base item → `Subscription.StripeBaseItemId`.
  (§8, §13 PR-4, B6.)
- **OQ-6 — OXXO → Resolved: Stripe Payment Method** (`ProviderKey='stripe'`,
  `IsAutomatic=true`); pivot to Conekta only if the concrete Stripe MX account can't
  enable it. (§4.1.)
- **OQ-7 — Templates → Resolved: code-owned in v2;** DB-editable post-v2 if demand
  emerges. (§10.)
- **OQ-8 — IVA → Resolved: backend computes** (16% MX, configurable); Stripe Tax is a
  future upgrade. Rationale: align with Fino's own CFDI (PR-7). (§11.)
- **OQ-9 — Refunds → Resolved: deferred, out of v2.** `Refunded` status stays a placeholder.
- **OQ-10 — Multi-currency → Resolved: future.** `Currency` columns ready; only MXN seeded.

---

## 15. References

- [payment-method-catalog-architecture.md](payment-method-catalog-architecture.md) — data-driven catalog paradigm + the §8b reseed footgun.
- [payment-method-catalog-api.md](payment-method-catalog-api.md) — wire-contract patterns (null omission, PascalCase enums, status codes).
- [backend-admin-surface-audit.md](backend-admin-surface-audit.md) — current admin backend state + the tensions this design resolves.
- [monetization-architecture.md](monetization-architecture.md) — device-limit enforcement SSoT; meets this doc at the add-on model (§8).
- `CLAUDE.md` — Offline Sync ("never reject valid orders") — applies to POS payments, not SaaS billing.
- Existing code: `Subscription.cs`, `SubscriptionItem.cs`, `SubscriptionController.cs`,
  `StripeService.cs`, `StripeWebhookController.cs`, `StripeEventProcessorWorker.cs`,
  `StripeConstants.cs`, `DeviceService.cs` (`EnforceDeviceLimitsAsync`), `Invoice.cs` (CFDI — not this).
- Pattern reuse: `AdminFeatureMatrixController.cs`, `PaymentMatrixAdminController.cs`,
  `FeatureMatrixAuditLog.cs`, `PaymentMatrixAuditLog.cs`, `PaymentMethodCacheGeneration`,
  `AuditInterceptor.cs`.

---

## NO in scope (v2)

- Implementation code (this is a design doc).
- Time estimates.
- Multi-currency (OQ-10, future).
- Detailed refunds (OQ-9, deferred).
- DB-editable notification templates (OQ-7, deferred post-v2). *(Editable catalog plan
  price — OQ-3 — is now **in** v2 via PR-1a.)*
