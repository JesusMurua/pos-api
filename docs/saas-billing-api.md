# SaaS Billing v2 — Admin Wire Contract (`saas-billing-api.md`)

> Companion to [saas-billing-architecture.md](saas-billing-architecture.md) §9. Documents the
> **as-built** admin surface (X-Admin-Token) shipped in PR-1a…PR-6. This is the source of truth
> for the fino-admin TypeScript models and service layer.
>
> **Status:** reflects code at commit `23f285a` (suite 250). Where the architecture doc promised an
> endpoint that was **not built**, it is listed under [§10 Gaps](#10-gaps--required-before-ui-1) —
> **read that section before scoping UI-1**: the BusinessAuditLog viewer, the subscription add-on
> list, and the billing-method / add-on catalogs do not yet have a read surface.

---

## 1. Conventions

| Concern | Rule |
|---|---|
| **Auth** | Every endpoint requires the `X-Admin-Token` header (scheme `AdminToken`). Missing/invalid → `401`. |
| **Property casing** | `camelCase` (`JsonNamingPolicy.CamelCase`). |
| **Enum casing** | PascalCase **strings** (`JsonStringEnumConverter`). E.g. invoice status `"Open"`, item type `"PlanBase"`, recipient `"Owner"`. |
| **Null handling** | `DefaultIgnoreCondition = WhenWritingNull` → **nullable fields are omitted from the JSON when null**. Model them in TS as optional (`field?: T`), never `T \| null`. |
| **Timestamps** | `DateTime` UTC, ISO 8601 (e.g. `"2026-06-11T05:12:46Z"`). |
| **Money — ⚠️ two shapes coexist** | Subscription / invoice / payment DTOs use **flat `…Cents` integers** plus a sibling `currency` string field. The **metrics** DTOs use a **`MoneyDto { amountCents, currency }` object**. This inconsistency is as-built; each section notes which it uses. Currency is always `"MXN"` today (OQ-10). |
| **Cycles** | `Subscription.billingCycle` ∈ `"Monthly" \| "Annual"` (plain strings, not an enum). `pricingGroup` ∈ `"General" \| "Standard" \| "Restaurant"`. |

### Enumerated string values

- **`Subscription.status`** (plain string, lowercase — Stripe values): `"active"`, `"trialing"`, `"past_due"`, `"canceled"`, `"paused"`, `"incomplete"`, `"incomplete_expired"`. For badges: active/trialing = healthy, past_due = warning, canceled/incomplete\* = inactive.
- **`SubscriptionInvoice.status`** (PascalCase enum): `"Open"`, `"PartiallyPaid"`, `"Paid"`, `"Overdue"`, `"Void"`, `"Refunded"` (`Refunded` is a placeholder — refunds deferred, OQ-9).
- **`SubscriptionInvoiceItem.itemType`**: `"PlanBase"`, `"AddOn"`, `"Discount"`, `"Adjustment"`.
- **`BusinessAuditAction`** (used in BusinessAuditLog — see §10 gap): `"Created"`, `"Suspended"`, `"Reactivated"`, `"PlanChanged"`, `"TrialExtended"`, `"PasswordReset"`, `"Impersonated"`, `"SubscriptionPriceChanged"`, `"AddOnActivated"`, `"AddOnDeactivated"`, `"InvoiceCreated"`, `"InvoiceVoided"`, `"PaymentRegistered"`, `"PaymentDeleted"`, `"CfdiToggled"`, `"NotificationSent"`, `"Other"`.

---

## 2. Subscription

### `GET /api/Admin/businesses/{businessId}/subscription`

Subscription detail + price history. Money is flat cents.

`200` → `SubscriptionDetailResponse`:

```jsonc
{
  "businessId": 12,
  "planTypeId": 2,
  "planTypeCode": "Basic",          // resolved code (NOT a display name — see §10 GAP-D)
  "baseAmountCents": 14900,         // nullable: omitted when null (Enterprise unpriced)
  "currency": "MXN",
  "billingMethodId": 3,             // nullable
  "billingMethodCode": "BankTransfer", // resolved code; omitted when method null
  "status": "active",
  "billingCycle": "Monthly",
  "pricingGroup": "General",
  "stripeSubscriptionId": "sub_...", // nullable (manual rails)
  "stripePriceId": "price_...",      // nullable
  "cfdiRequired": false,
  "billingEmail": "owner@x.com",     // nullable
  "notes": "…",                      // nullable
  "nextBillingDate": "2026-07-01T00:00:00Z", // nullable
  "priceHistory": [
    {
      "id": 5, "beforeAmountCents": 14900, "afterAmountCents": 25000,
      "changedAtUtc": "2026-06-10T…", "changedByTokenId": "ab12cd34",
      "reason": "Negociado", "effectiveDate": "2026-06-10T…"
    }
  ]
}
```

- `404` if the business has no subscription.
- **⚠️ GAP-B:** the response does **not** include the subscription's active add-ons, and there is no separate "list add-ons" endpoint. A Tenant detail screen cannot show active add-ons today. See §10.

### `PUT /api/Admin/businesses/{businessId}/subscription`

Remote-first reconcile (Stripe rail: hits Stripe → 2xx → persists; manual rail: local only). Records `SubscriptionPriceHistory` + `BusinessAuditLog`.

Body `AdminUpdateSubscriptionRequest` — **only supplied fields change** (all optional):

```jsonc
{ "planTypeId": 3, "baseAmountCents": 25000, "billingMethodId": 3,
  "cfdiRequired": true, "billingEmail": "x@y.com", "notes": "…", "reason": "Upgrade" }
```

- `204` on success · `400` validation (e.g. Stripe rail with no base item to reprice) · `404` no subscription.

### `POST /api/Admin/businesses/{businessId}/subscription/add-ons`

Activate an add-on. Stripe rail = remote-first; manual rail = local only.

Body `AdminActivateAddOnRequest`:

```jsonc
{ "addOnId": 7, "quantity": 2, "customPriceCents": 5000, "reason": "…" }
```

- `quantity` default `1`. `customPriceCents` optional → creates a custom Stripe Price on the Stripe rail; absent → reuses the catalog `PlanAddOn.StripePriceId`.
- `204` · `400` price-less Stripe activation (catalog price null + no custom) · `404` business/add-on not found · `409` add-on already active on this subscription.

### `DELETE /api/Admin/businesses/{businessId}/subscription/add-ons/{subscriptionAddOnId}`

Soft-deactivate (sets `DeactivatedAt`). Stripe rail removes the Stripe item + archives a custom Price post-success.

- `204` · `404` active SubscriptionAddOn not found for this business.

---

## 3. Invoicing & Payments

> Money: **flat cents** throughout. `status` is the PascalCase `SubscriptionInvoice` enum (§1).

### `GET /api/Admin/businesses/{businessId}/invoices`

`200` → `AdminInvoiceListItemDto[]` (newest first):

```jsonc
[{ "id": 9, "invoiceNumber": 1, "status": "Open",
   "issuedAtUtc": "…", "dueDate": "…", "periodStart": "…", "periodEnd": "…",
   "subtotalCents": 14900, "taxCents": 2384, "totalCents": 17284,
   "paidCents": 0, "currency": "MXN", "stripeInvoiceId": null }]
```

### `POST /api/Admin/businesses/{businessId}/invoices`

Create a manual invoice (assigns `invoiceNumber`, computes IVA, opens it). Body `AdminCreateInvoiceRequest`:

```jsonc
{ "periodStart": "…", "periodEnd": "…", "dueDate": "…", "reason": "…",
  "items": [ { "description": "Plan base", "quantity": 1, "unitAmountCents": 14900,
               "itemType": "PlanBase", "linkedAddOnId": null, "linkedPlanTypeId": 2 } ] }
```

- `periodStart/End`, `dueDate` optional (defaulted server-side). `items` required (≥1).
- `201` → `AdminInvoiceDetailDto` (§ below) · `400` empty items · `404` business has no subscription.

### `GET /api/Admin/invoices/{id}`

`200` → `AdminInvoiceDetailDto`:

```jsonc
{ "id": 9, "subscriptionId": 4, "businessId": 12, "invoiceNumber": 1, "status": "PartiallyPaid",
  "issuedAtUtc": "…", "dueDate": "…", "periodStart": "…", "periodEnd": "…",
  "subtotalCents": 14900, "taxCents": 2384, "totalCents": 17284, "paidCents": 10000,
  "currency": "MXN", "stripeInvoiceId": null,
  "items": [ { "id": 1, "description": "Plan base", "quantity": 1, "unitAmountCents": 14900,
               "totalAmountCents": 14900, "itemType": "PlanBase",
               "linkedAddOnId": null, "linkedPlanTypeId": 2 } ],
  "payments": [ { "id": 1, "billingMethodId": 3, "amountCents": 10000, "currency": "MXN",
                  "paidAtUtc": "…", "reference": "FOLIO-1", "notes": null,
                  "isAutomatic": false, "stripeChargeId": null } ] }
```

- `404` if not found.

### `PUT /api/Admin/invoices/{id}`

Edit while **Open** only. Body `AdminUpdateInvoiceRequest`: `{ "dueDate": "…", "reason": "…" }` (both optional).
- `204` · `400` invoice not Open · `404`.

### `POST /api/Admin/invoices/{id}/void`

Body `AdminVoidInvoiceRequest`: `{ "reason": "…" }`.
- `204` · `400` invoice not in `{Open, Overdue}` (a PartiallyPaid invoice must have its payments deleted first) · `404`.

### `POST /api/Admin/invoices/{id}/payments`

Record a payment (write-time status recompute). Body `AdminRecordPaymentRequest`:

```jsonc
{ "billingMethodId": 3, "amountCents": 14900, "currency": "MXN",
  "paidAtUtc": "…", "reference": "FOLIO-1", "notes": "…" }
```

- `paidAtUtc` optional (defaults now). Idempotent on `(billingMethodId, reference)` when reference present.
- `204` · `400` currency mismatch / Void/Refunded invoice · `404`.

### `DELETE /api/Admin/invoices/{id}/payments/{paymentId}`

Capture-error fix; recomputes status. `204` · `404`.

---

## 4. Catalogs

### `GET /api/Admin/plan-types` · `PUT /api/Admin/plan-types/{id}`

`GET 200` → `PlanTypeDto[]` (Id, Code, Name, MonthlyPrice (decimal pesos), SortOrder, IsActive — see the Catalog contract). `PUT` body `AdminUpdatePlanTypeRequest` (editable: MonthlyPrice/Name/SortOrder/Currency; Code/Id immutable) → `204` · `400` (e.g. negative price) · `404`. Edits invalidate the `PlanTypes` + `Plans` public caches.

> **⚠️ GAP-C:** `GET /api/Admin/billing-methods` (SaaSBillingMethod catalog) and `GET /api/Admin/plan-add-ons` (PlanAddOn catalog) — promised in architecture §9 — **were never built**. UI dropdowns for rail selection and add-on selection have no data source. See §10.

---

## 5. Metrics

> Money: **`MoneyDto { amountCents, currency }` objects** here (unlike §2/§3). Non-realtime.

### `GET /api/Admin/billing/metrics?lookback=12`

`200` → `AdminBillingMetricsDto`:

```jsonc
{
  "currentMrr": { "amountCents": 350000, "currency": "MXN" },
  "currentArr": { "amountCents": 4200000, "currency": "MXN" },
  "asOf": "2026-06-11T…",
  "activeSubscriptions": 12, "trialSubscriptions": 3, "pastDueSubscriptions": 1,
  "churnRate30d": 0.02,                 // 0..1, paid-logo churn
  "revenueByMonth": [ { "month": "2026-05", "amountCents": 280000, "currency": "MXN" } ],
  "retentionByCohort": [
    { "cohortMonth": "2026-01", "cohortSize": 5,
      "periods": [ { "period": 0, "rate": 1.0 }, { "period": 1, "rate": 0.8 } ] } ],
  "notificationStats": { "failed24h": 0, "failed7d": 0, "failedTotal": 0,
                         "pending": 4, "oldestPendingAgeMinutes": 2 }
}
```

- `lookback` 1..60, default 12. MRR/ARR are a **current snapshot** (no historical MRR series); `revenueByMonth` is **collected** cash; retention is reconstructed from `CanceledAt`. `oldestPendingAgeMinutes` rising ⇒ the dispatch worker is down.

### `GET /api/Admin/billing/upcoming-invoices?days=30`

`200` → `UpcomingInvoiceDto[]`:

```jsonc
[{ "businessId": 12, "subscriptionId": 4, "nextBillingDate": "…",
   "estimatedAmountCents": 20900, "currency": "MXN", "rail": "BankTransfer" }]
```

- `days` 1..365, default 30. **Manual rail only** — Stripe-rail upcoming invoices live in the Stripe Dashboard (authoritative for prorations).

---

## 6. Notifications

### `GET /api/Admin/notification-templates`

`200` → `[{ "code": "Welcome", "defaultRecipient": "Custom" }, …]` (15 code-owned es-MX templates).

### `POST /api/Admin/businesses/{businessId}/notify`

Manual send (immediate dispatch + writes a `NotificationSent` audit row). Body `AdminNotifyRequest`:

```jsonc
{ "templateCode": "PlanChanged", "payload": { "oldPlan": "Basic", "newPlan": "Pro" } }
```

- `204` · `404` template/business not found. `payload` keys must match the template (a missing key fails the row at dispatch time, not here).

---

## 7. Audit blobs (`before` / `after`)

The `BusinessAuditLog` rows carry `beforeJson` / `afterJson` — **opaque JSON strings**, serialized server-side from anonymous shapes that vary per action. **Do not type-parse them on the client**; render as collapsible raw JSON. Caveat (mirrors the payment-method doc M5): enum-ish values **inside** these blobs are whatever the server wrote (often raw ints), even though the same concept is a PascalCase string on the typed wire endpoints. (Moot until the read endpoint exists — §10 GAP-A.)

---

## 8. Status codes (summary)

| Code | Meaning |
|---|---|
| 200 / 201 / 204 | Success (read / created / no-content mutation) |
| 400 | Validation (bad input, illegal state transition, currency mismatch, price-less Stripe activation) |
| 401 | Missing/invalid `X-Admin-Token` |
| 404 | Resource not found |
| 409 | Conflict (add-on already active) |

---

## 9. Money-shape reference (for TS models)

```ts
// §2 subscription, §3 invoices/payments — flat cents + sibling currency:
interface Money { amountCents: number; currency: string }   // §5 metrics ONLY
// elsewhere: e.g. baseAmountCents: number; currency: string  (separate fields)
```

---

## 10. Gaps — required before UI-1

The architecture doc §9 listed these; the code audit (2026-06-11) confirms they were **never built**. They block the fino-admin Tenant-detail + audit-viewer screens and should ship as a short backend PR **before** UI-1.

| # | Gap | Blocks | Proposed contract |
|---|---|---|---|
| **GAP-A** | **No BusinessAuditLog read endpoints.** `GET /Admin/businesses/{id}/audit-log` and `GET /Admin/audit-log` do not exist (only FeatureMatrix/PaymentMatrix audit logs have readers). The rows are written since PR-1a but have no read surface. | BusinessAuditLog viewer | Paginated: `?page&pageSize&action?&from?&to?` → envelope `{ page, pageSize, totalRows, items: [{ id, businessId, action, reason?, beforeJson?, afterJson?, changedByTokenId?, changedAtUtc }] }`. Mirror the `FeatureMatrixAuditLog.GetAuditLogAsync` shape. |
| **GAP-B** | **`GET /subscription` omits active add-ons**, and there is no list-add-ons endpoint. | Add-on display on Tenant detail | Add `addOns: [{ id, addOnId, code, name, quantity, effectivePriceCents (customPriceCents ?? defaultPriceCents), activatedAt, stripeItemId? }]` to `SubscriptionDetailResponse`. |
| **GAP-C** | **No admin catalog readers** for `SaaSBillingMethod` (`GET /Admin/billing-methods`) or `PlanAddOn` (`GET /Admin/plan-add-ons`). | Rail + add-on dropdowns | `GET /Admin/billing-methods` → `[{ id, code, name, isAutomatic, requiresReference, isActive }]`; `GET /Admin/plan-add-ons` → `[{ id, code, name, billingCycle, defaultPriceCents, currency, linkType, isActive }]`. |
| **GAP-D** | **Denormalization is code-level, not name-level.** `GET /subscription` returns `planTypeCode` + `billingMethodCode` but no display names / billing-method flags. | Labels without extra round-trips | Either embed display fields in `SubscriptionDetailResponse` (plan name, billing-method name/isAutomatic/requiresReference) **or** ship GAP-C so the UI maps codes→labels from cached catalogs. GAP-C is the lighter fix and unblocks both. |

**Recommendation:** a short **PR-UI-prep** (read-only endpoints: GAP-A audit-log readers, GAP-B add-ons in subscription detail, GAP-C two catalog readers) lands before UI-1. GAP-D is then satisfied by GAP-C (code→label mapping client-side) without changing existing response shapes.
