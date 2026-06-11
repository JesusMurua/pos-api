# SaaS Billing v2 — Admin Wire Contract (`saas-billing-api.md`)

> Companion to [saas-billing-architecture.md](saas-billing-architecture.md) §9. Documents the
> **as-built** admin surface (X-Admin-Token) shipped in PR-1a…PR-6. This is the source of truth
> for the fino-admin TypeScript models and service layer.
>
> **Status:** reflects code after **PR-UI-prep** + the **subscription-create MINI-PR** (suite 265). The
> five read surfaces the architecture doc §9 promised but that were missing at `23f285a` —
> BusinessAuditLog readers, active add-ons on the subscription detail, and the billing-method / add-on
> catalog readers — are **built** ([§10](#10-gaps--resolved-in-pr-ui-prep)); plus `POST /subscription`
> (create flow) and `stripeCustomerId` on the subscription detail (§2). UI-1 and UI-2 are unblocked.

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
- **`BusinessAuditAction`** (used in BusinessAuditLog — readable via §7): `"Created"`, `"Suspended"`, `"Reactivated"`, `"PlanChanged"`, `"TrialExtended"`, `"PasswordReset"`, `"Impersonated"`, `"SubscriptionCreated"`, `"SubscriptionPriceChanged"`, `"AddOnActivated"`, `"AddOnDeactivated"`, `"InvoiceCreated"`, `"InvoiceVoided"`, `"PaymentRegistered"`, `"PaymentDeleted"`, `"CfdiToggled"`, `"NotificationSent"`, `"Other"`.

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
  "stripeCustomerId": "cus_...",     // nullable (null on manual rails)
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
  ],
  "activeAddOns": [
    {
      "subscriptionAddOnId": 8, "addOnId": 7, "addOnCode": "device_kds",
      "addOnName": "Pantalla KDS adicional", "quantity": 2,
      "customPriceCents": 5000,           // nullable: omitted when on catalog price
      "defaultPriceCents": 4900,
      "effectivePriceCents": 5000,        // customPriceCents ?? defaultPriceCents (server-resolved)
      "billingCycle": "Monthly", "activatedAt": "2026-06-08T…"
    }
  ]
}
```

- `404` if the business has no subscription.
- `activeAddOns` (PR-UI-prep, GAP-B): **active add-ons only** (`DeactivatedAt IS NULL`); deactivated rows are kept for history but never returned here. `effectivePriceCents` is resolved server-side so the UI shows the charged amount without re-deriving it. There is no separate "list add-ons" endpoint — they ride on the subscription detail.

### `POST /api/Admin/businesses/{businessId}/subscription`

Provision a subscription where **none exists** (the PUT only edits an existing one). Stripe rail = remote-first (creates the Stripe Customer if absent + a Subscription against the **catalog** Price for `(planTypeId, "Monthly", business pricing group)`, awaits 2xx, then persists a local row mirroring Stripe); manual rail = local only. Records a `SubscriptionCreated` `BusinessAuditLog` row and keeps `Business.planTypeId` in sync.

Body `AdminCreateSubscriptionRequest`:

```jsonc
{
  "planTypeId": 2,            // required
  "billingMethodId": 3,      // required
  "baseAmountCents": 14900,  // optional (null = unset, e.g. Enterprise)
  "currency": "MXN",         // default "MXN"
  "cfdiRequired": false,     // default false
  "billingEmail": "x@y.com", // optional
  "notes": "…",              // optional
  "reason": "Alta manual"    // optional → BusinessAuditLog
}
```

- The created subscription is **Monthly**; its pricing group is derived from the business macro-category (self-service owns cycle choice). `baseAmountCents` is the local per-tenant SSoT; on the Stripe rail the Stripe charge follows the **catalog** price (push a negotiated amount afterward via the PUT reprice flow).
- `201 Created` → `SubscriptionDetailResponse` of the new subscription (`Location` header points at the GET).
- `400` validation (unknown `planTypeId` / inactive `billingMethodId`, negative `baseAmountCents`, **Stripe rail with no catalog price** — e.g. Enterprise → use a manual rail).
- `409` the business already has a subscription (use PUT to edit).
- `502` the Stripe SDK rejected/failed the create (nothing is persisted — retry).

### `PUT /api/Admin/businesses/{businessId}/subscription`

Remote-first reconcile (Stripe rail: hits Stripe → 2xx → persists; manual rail: local only). Records `SubscriptionPriceHistory` + `BusinessAuditLog`.

Body `AdminUpdateSubscriptionRequest` — **only supplied fields change** (all optional):

```jsonc
{ "planTypeId": 3, "baseAmountCents": 25000, "billingMethodId": 3,
  "cfdiRequired": true, "billingEmail": "x@y.com", "notes": "…", "reason": "Upgrade" }
```

- **`baseAmountCents` is preserved when changing `planTypeId`** — it is a per-tenant SSoT (architecture §4.3), so a negotiated price survives a plan change. To reset it to the new plan's default, set `baseAmountCents` to the new value explicitly **in the same request**. A null/absent field is always a no-op, never a reset — there is no way to express "clear to default" via null (an absent field and an explicit `null` are indistinguishable on the wire).
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

### `GET /api/Admin/billing-methods`

The SaaS billing rails (tenant → operator), ordered by `sortOrder`. Read-only, code-seeded. `200` → `SaaSBillingMethodDto[]`:

```jsonc
[{ "id": 1, "code": "Stripe", "name": "Stripe", "isAutomatic": true, "requiresReference": false,
   "providerKey": "stripe", "countryCode": null, "sortOrder": 1, "isActive": true, "isSystem": true }]
```

- 7 rails today (`Stripe`, `BankTransfer`, `OxxoPay`, `Cash`, `BankDeposit`, `Check`, `Other`). Use `code` as the stable selector key; `name` is the display label.

### `GET /api/Admin/plan-add-ons`

The full add-on catalog (active **and** inactive), ordered by id. Read-only, code-seeded. `200` → `PlanAddOnDto[]`:

```jsonc
[{ "id": 7, "code": "device_kds", "name": "Pantalla KDS adicional", "description": null,
   "billingCycle": "Monthly", "defaultPriceCents": 4900, "currency": "MXN",
   "linkType": "DeviceLicense", "linkedEntityId": 14, "stripePriceId": "price_…",
   "isActive": true, "isSystem": true }]
```

- `billingCycle` ∈ `"OneTime" | "Monthly" | "Annual"`. `linkType` ∈ `"DeviceLicense" | …`. `linkedEntityId` is polymorphic (for `DeviceLicense` it is a `FeatureKey` int — opaque to the UI).

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

## 7. Audit log (read) + blobs

### `GET /api/Admin/businesses/{businessId}/audit-log` · `GET /api/Admin/audit-log`

Read the `BusinessAuditLog` trail (the explicit operator-action log, written since PR-1a). The per-tenant route is scoped to one business; the cross-tenant route spans all tenants and takes an optional `businessId`. Both are paginated and newest-first.

Query: `?page=1&pageSize=50&action=PlanChanged&from=2026-01-01&to=2026-12-31` — `action`/`from`/`to` optional (cross-tenant also takes `businessId`). `page` defaults `1`, `pageSize` defaults `50` (max `200`). An unknown `action` name yields **zero rows** (typed filter — no silent wildcard).

`200` → `PagedBusinessAuditLogDto`:

```jsonc
{
  "page": 1, "pageSize": 50, "totalRows": 3,
  "items": [
    { "id": 12, "businessId": 12, "action": "PlanChanged",
      "changedAtUtc": "2026-06-10T…", "changedByTokenIdHash": "ab12cd34",
      "reason": "Upgrade",             // nullable
      "beforeJson": "{…}", "afterJson": "{…}" }  // nullable opaque strings — see below
  ]
}
```

- `action` is the stable PascalCase `BusinessAuditAction` name (§1). `changedByTokenIdHash` is the hashed admin `token_id` (display as an opaque attribution chip).

### Audit blobs (`before` / `after`)

The `beforeJson` / `afterJson` fields are **opaque JSON strings**, serialized server-side from anonymous shapes that vary per action. **Do not type-parse them on the client**; render as collapsible raw JSON. Caveat (mirrors the payment-method doc M5): enum-ish values **inside** these blobs are whatever the server wrote (often raw ints), even though the same concept is a PascalCase string on the typed wire endpoints.

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

## 10. Gaps — resolved in PR-UI-prep

The architecture doc §9 promised these read surfaces but they were missing at `23f285a`. **PR-UI-prep** (read-only, no migrations) shipped all four; UI-1 is unblocked. Kept here as a resolution record.

| # | Gap | Status | Where |
|---|---|---|---|
| **GAP-A** | No BusinessAuditLog read endpoints. | ✅ **Shipped** | [§7](#7-audit-log-read--blobs): `GET /Admin/businesses/{id}/audit-log` + `GET /Admin/audit-log`, paginated + filterable. |
| **GAP-B** | `GET /subscription` omitted active add-ons. | ✅ **Shipped** | [§2](#2-subscription): `activeAddOns[]` on `SubscriptionDetailResponse` (active only, server-resolved `effectivePriceCents`). |
| **GAP-C** | No admin catalog readers for rails / add-ons. | ✅ **Shipped** | [§4](#4-catalogs): `GET /Admin/billing-methods` + `GET /Admin/plan-add-ons`. |
| **GAP-D** | Denormalization is code-level, not name-level. | ✅ **Resolved by GAP-C** | `GET /subscription` still returns `planTypeCode` / `billingMethodCode`; the UI maps codes→labels from the cached GAP-C catalogs. No existing response shape changed. |
