# Backend Admin-Surface Audit — Gap Analysis Input

> **Point-in-time inventory** of the pos-api backend as seen by the super-admin
> operator (fino-admin console). Captured **2026-06-07**. Read-only audit — no
> code changed. Cross-reference with the fino-admin UI audit to build the
> Backend × UI × Gap priority matrix.
>
> Scope question it answers: *what admin/operator capability exists in the
> backend, what has no UI, and what genuinely does not exist* — with a focus on
> the monetization loop (activate feature → raise charge → invoice → notify).

---

## 0. Headline — billing exists, but is tenant-self-service

The initial hypothesis ("no billing module") is **false, with a large
asterisk**:

- **Subscription billing via Stripe is COMPLETE** for the base plan: entities
  `Subscription` / `SubscriptionItem`, `StripeService`, `SubscriptionController`,
  `StripeWebhookController`, background worker `StripeEventProcessorWorker`, and
  24 real base-plan price IDs.
- **`PlanTypeCatalog.MonthlyPrice` exists and is seeded**: Free=$0, Basic=$149,
  Pro=$349, Enterprise=`null` (contact-sales), currency MXN
  ([DbInitializer.cs:40-47](../POS.Repository/DbInitializer.cs#L40-L47)).
- **BUT the charge is initiated by the TENANT** from fino-app (Stripe Checkout).
  There is **not a single `/Admin/billing/*` or `/Admin/subscription/*`
  endpoint**. The super-admin cannot, from the backend: raise the charge, add an
  add-on, issue/invoice, or notify.
- Therefore the monetization use case *"activate MercadoPago → raise the monthly
  charge → invoice the add-on → notify"* is **✗ end-to-end from fino-admin** —
  not for lack of billing, but for lack of an **admin operating surface over the
  billing**.

---

## 1. Admin endpoints `/api/Admin/*`

All authenticated via the `X-Admin-Token` header
(`AdminTokenAuthenticationHandler`, scheme `"AdminToken"`); forensic audit uses a
hashed `token_id`.

### AdminBusinessesController — `/api/Admin/businesses` (rate-limit 30/min/IP)

| Endpoint | Purpose | Entities | Audit |
|---|---|---|---|
| `POST /api/Admin/businesses` | Atomic tenant alta (Business+matrix Branch+Owner+seed) | Business, Branch, User, UserBranch, Zone, RestaurantTable, BusinessGiro | Yes (structured log) |
| `GET /api/Admin/businesses` | Cross-tenant paginated directory (plan/macro/active/trial filters) | Business `IgnoreQueryFilters`, Users(Owner) | Yes |
| `GET /api/Admin/businesses/stats` | Cross-tenant aggregate stats | Business/Users/Products `IgnoreQueryFilters` | Yes |
| `GET /api/Admin/businesses/{id}` | Full tenant detail | Business `IgnoreQueryFilters` | Yes |
| `PATCH /api/Admin/businesses/{id}/status` | Suspend/reactivate (`IsActive`) | Business.IsActive + invalidates feature cache | Yes |
| `PATCH /api/Admin/businesses/{id}/plan` | Change plan (raw FK) | Business.PlanTypeId + invalidates cache | Yes |
| `PATCH /api/Admin/businesses/{id}/trial` | Extend trial (≤180 days) | Business.TrialEndsAt | Yes |
| `POST /api/Admin/businesses/{id}/reset-owner-password` | Reset owner password (returns plaintext) | User.PasswordHash | Yes |
| `POST /api/Admin/businesses/{id}/impersonate` | Login-as: 2h Owner JWT | (mints session) | Yes (LogWarning) |

### AdminFeatureMatrixController — `/api/Admin/*` (14 endpoints)

`feature-catalog` GET/PUT · `plan-feature-matrix` GET/PUT ·
`business-type-feature-matrix` GET/PUT · `cluster-feature-matrix` GET/PUT ·
`plan-business-type-overrides` GET/POST/PUT/DELETE ·
`feature-matrix/preview-impact` GET · `feature-matrix/audit-log` GET.

### PaymentMatrixAdminController — `/api/Admin/*` (12 endpoints, PR-B)

`payment-method-catalog` GET/POST/PUT/DELETE · `plan-payment-method-matrix`
GET/PUT · `tenant-payment-method-overrides` GET/POST/PUT/DELETE ·
`payment-matrix/preview-impact` GET · `payment-matrix/audit-log` GET.
See [payment-method-catalog-api.md](payment-method-catalog-api.md) for the wire contract.

### AdminOrdersController

`GET /api/Admin/orders/unauthorized-methods` — cross-tenant payment drift report (PR-A2).

### AdminCatalogController

`POST /api/Admin/catalogs/invalidate` — evict cache (whitelist of 11 keys, rate-limit 10/min/IP).

**Cross-tenant access** uses `IgnoreQueryFilters()` in `BusinessRepository`
(`GetAllForAdminAsync`, `GetByIdForAdminAsync`, `GetAdminStatsAsync`) and
`OrderRepository.GetFlaggedPaymentsAsync`.

---

## 2. Catalog endpoints `/api/Catalog/*` (read-only)

`CatalogController` — 11 `[AllowAnonymous]` endpoints: kitchen-statuses,
display-statuses, payment-methods, device-modes, business-types,
macro-categories, zone-types, **plan-types**, **plans** (with features),
access-reasons, access-methods. Envelope `CatalogResponse<T>` + ETag/Cache-Control.
Also: `GET /api/Taxes?countryCode=` `[Authorize]`;
`GET /api/payment-methods/available` `[Authorize]` (PR-B).

> `/api/Catalog/plan-types` and `/api/Catalog/plans` already expose plans **with
> price**, but are anonymous + read-only (no edit surface).

---

## 3. Tenancy data model

- **`Business`** ([Business.cs](../POS.Domain/Models/Business.cs)): `PlanTypeId`,
  `TrialEndsAt?`, `TrialUsed`, `IsActive` (suspension = `IsActive=false`),
  `OnboardingCompleted`/`OnboardingStatusId`/`CurrentOnboardingStep`, `CreatedAt`,
  `CountryCode`, fiscal (`Rfc?`, `TaxRegime?`, `LegalName?`, `InvoicingEnabled`,
  `FacturapiOrganizationId?`), `PrimaryMacroCategoryId`, `CustomGiroDescription?`.
  Nav `Subscription` (1:1).
  - ❌ **No suspension reason/date field** — only the `IsActive` bool.
- **Owner** = `User` with `RoleId = 1` (UserRoleIds.Owner). No Owner table;
  multiple owners possible; admin picks first by `CreatedAt`.
- **`Branch`**: FK `BusinessId` (1:N), `IsMatrix`, `IsActive`, `TimeZoneId`,
  `FolioCounter/Prefix/Format`, `FiscalZipCode?`, `HasKitchen/HasTables/HasDelivery`.

---

## 4. Plans & pricing

- **`PlanTypeCatalog`**: `Id`, `Code`, `Name`, `SortOrder`,
  **`MonthlyPrice decimal(10,2)?`**, `Currency` (default "MXN"). Seeded fixed
  Free=1 / Basic=2 / Pro=3 / Enterprise=4.
- ❌ **No quota fields** on PlanType → limits live in `FeatureCatalog` +
  `PlanFeatureMatrix.DefaultLimit` (MaxUsers/MaxBranches/MaxProducts/MaxDevices…),
  overridable via `PlanBusinessTypeFeatureOverride`.
- ❌ **No admin endpoint to edit PlanType** (price included). Editing a price ⇒
  direct DB mutation. `PATCH /businesses/{id}/plan` only reassigns the tenant FK.
- ❌ **No `PricingTier`, `Coupon`, `Discount`.** Only pricing-related extra is
  `Subscription` (Stripe).

---

## 5. Billing module — COMPLETE (Stripe), tenant-self-service

| Piece | State | Evidence |
|---|---|---|
| `Subscription` + `SubscriptionItem` (base plan + add-ons) | ✅ | [Subscription.cs](../POS.Domain/Models/Subscription.cs); migrations `AddSubscription`/`AddSubscriptionItemsTable` |
| `SubscriptionService` / `StripeService` | ✅ | checkout, cancel, status, webhook-queue |
| `SubscriptionController` | ✅ tenant | `GET /api/subscription/status`, `POST /api/subscription/checkout` (Owner), `POST /api/subscription/cancel` |
| `StripeWebhookController` | ✅ | `POST /api/stripe/webhook` (Anonymous + HMAC) → `StripeEventInbox` |
| `StripeEventProcessorWorker` | ✅ | BackgroundService poll 5s; checkout.session.completed / subscription.updated/deleted / invoice.payment_failed/succeeded; temporal guards by `UpdatedAt` |
| `/Admin/billing/*` or `/Admin/subscription/*` | ❌ **Do not exist** | Admin does not operate billing |
| Add-on device-licensing | 🟡 **partial** | add-on price IDs are `price_dummy_*` placeholders ([StripeConstants.cs:164-169](../POS.Domain/Helpers/StripeConstants.cs#L164-L169)); base plan has 24 real IDs |

---

## 6. Subscription PSP (charging the tenant)

- ✅ **Stripe.net v51** in `POS.API` and `POS.Services`. Config
  `Stripe:SecretKey/PublishableKey/WebhookSecret`; env `STRIPE_SECRET_KEY` /
  `STRIPE_WEBHOOK_SECRET`. Keys empty in `appsettings.json` (injected via env in prod).
- ❌ **Zero** Conekta / OpenPay / MercadoPago-Suscripciones / Recurly / Paddle.
- ⚠️ **Do not confuse:** `IClipService` / `IMercadoPagoService` +
  `PaymentWebhookProcessorWorker` are **POS-side** (end customer → tenant), NOT
  subscription. `IInvoicingService`/Facturapi (`Invoice`) is **CFDI** (tenant →
  their customer), also not SaaS-billing.

---

## 7. Tenant lifecycle map

| Action | Endpoint | Mutates | Exact behavior | Audit |
|---|---|---|---|---|
| Create (admin) | `POST /Admin/businesses` | Business+Branch+Owner+seed | Atomic via `IAuthService.RegisterAsync` | Yes (log) |
| Create (self-signup) | `POST /api/auth/register` (public, NOT admin) | idem | — | — |
| Suspend/reactivate | `PATCH /Admin/businesses/{id}/status` | `IsActive` | Blocks auth + invalidates feature cache | Yes (log) |
| Change plan | `PATCH /Admin/businesses/{id}/plan` | `PlanTypeId` | **Writes FK only + invalidates cache. Does NOT call Stripe.** | Yes (log) |
| Trial extend | `PATCH /Admin/businesses/{id}/trial` | `TrialEndsAt` | Validates future & ≤180d | Yes (log) |
| Trial expire/convert | — | — | ❌ **No endpoint/job**; expires by date comparison in queries | — |
| Cancel/delete | — | — | ❌ **No admin delete/cancel** (tenant cancels Stripe via `/subscription/cancel`) | — |
| Reset owner password | `POST /Admin/businesses/{id}/reset-owner-password` | `User.PasswordHash` | Returns plaintext | Yes (log) |
| Login-as | `POST /Admin/businesses/{id}/impersonate` | — | Owner JWT TTL **2h**, normal user claims | Yes (LogWarning) |
| Edit contact/fiscal | ❌ **Not admin** | — | Via **tenant** endpoints `PUT /api/Business/settings\|giro\|fiscal` | — |

> ⚠️ **Consistency tension:** `PATCH /plan` (admin) does a raw FK write while
> **Stripe is the source of truth** (the worker syncs `Business.PlanTypeId` from
> webhooks). An admin plan change can **diverge from what the tenant actually
> pays** or be **overwritten** by the next webhook. There is no admin↔Stripe
> reconciliation.

---

## 8. Audit logs

- **3 audit tables (silos):** `AuditLog` (tenant entities Product/Category/Order/
  Branch via `AuditInterceptor`), `FeatureMatrixAuditLog`, `PaymentMatrixAuditLog`.
- ❌ **No general admin audit log.** §7 actions (suspend/plan/reset/impersonate/
  create) go to **structured logs (Serilog) only**, not a queryable table.
- ❌ **No `/Admin/audit-log` general** (only the two matrix-specific ones).
  ❌ **No per-business timeline.**

---

## 9. Cross-tenant metrics

- ✅ `GET /Admin/businesses/stats`: totals, plan/macro distribution, trials
  expiring 7/14d, onboarding, TotalUsers/Products, creation-by-month (6 months).
- ✅ `GET /Admin/businesses` (filterable directory). ✅ drift
  `/Admin/orders/unauthorized-methods`.
- ❌ **Do not exist:** MRR/ARR/revenue, churn, cohort retention, country
  distribution, LTV. Stats are **count/distribution**, not financial — despite
  the price living in the DB.

---

## 10. Notifications / email

- ✅ **`EmailService` via Resend** (`Email:ApiKey` / `RESEND_API_KEY`).
  Fire-and-forget, 10s timeout, **no queue, no retries**.
- ✅ **Welcome template only** ([EmailService.cs:35-79](../POS.Services/Service/EmailService.cs#L35-L79)),
  gated by `SuppressWelcomeEmail` (default `true`; sends when `false`). Confirmed by tests.
- ❌ **Do not exist:** suspension, plan-change, invoice, trial-expiring,
  password-reset, onboarding.
- ❌ **No `POST /Admin/businesses/{id}/notify`** nor any manual-notify endpoint.

---

## 11. Admin endpoints without UI (cross vs the 5 known fino-admin screens)

Known UI exists for: businesses CRUD/lifecycle, feature-catalog,
plan-feature-matrix, business-type-feature-matrix, cluster-feature-matrix,
plan-business-type-overrides, feature-matrix preview+audit.

**Backend admin WITHOUT fino-admin UI (console gaps):**

- 🔲 **All of PaymentMatrixAdminController (12 endpoints, PR-B)** — payment-method
  catalog, plan×method, per-tenant overrides, preview, audit. **No UI.**
- 🔲 `GET /Admin/orders/unauthorized-methods` (drift report) — **no UI.**
- 🔲 `POST /Admin/catalogs/invalidate` — **no UI** (manual ops).
- 🔲 `GET /Admin/businesses/stats` — backend exists; **verify it has a dashboard tile.**
- 🔲 `GET /Admin/payment-matrix/audit-log` — no UI (parallel to the feature-matrix one that does have UI).

**Non-admin backend the operator would still need, with no admin surface:**
subscription (tenant only), plan/price editing (DB only), manual notify (does
not exist).

---

## 12. Multi-tenancy plumbing

- **Tenant resolution:** `HttpTenantContext`
  ([HttpTenantContext.cs](../POS.Repository/Tenancy/HttpTenantContext.cs)) reads
  JWT claims `businessId`/`branchId` dynamically per request (no subdomain, no
  header). Null in background jobs → filters become no-op (workers see all).
- **Global query filters:** `IBranchScoped`/`IBusinessScoped` applied in
  `ApplicationDbContext` (`tenant == null || e.scope == tenant`, Npgsql-safe).
  Insert-time `BranchInjectionInterceptor` overwrites `BranchId` from the claim
  (zero-trust).
- ❌ **No admin per-tenant config endpoints** (locale/currency/timezone/ad-hoc
  flags) outside feature-matrix. Currency hardcoded MX; timezone set per-branch
  at creation. Tenant config = **tenant** endpoints (`Business/settings|giro|
  fiscal`, `Branch/...`), not admin.

---

## 13. Backend × UI × Gap matrix

| Capability | Backend | fino-admin UI | Gap |
|---|---|---|---|
| Tenant alta/lifecycle | ✅ | ✅ | — |
| Feature matrix | ✅ | ✅ | — |
| **Payment matrix (PR-B)** | ✅ | ❌ | **UI** |
| Change tenant plan | ✅ (raw FK) | ✅ | ⚠️ no Stripe reconciliation |
| **Raise charge / add-on / invoice the tenant** | 🟡 tenant self-service; ❌ admin | ❌ | **admin surface + dummy add-ons** |
| Edit plan price | ❌ (DB only) | ❌ | **entity ok, missing endpoint+UI** |
| Payment drift report | ✅ | ❌ | **UI** |
| Financial metrics (MRR/churn) | ❌ | ❌ | **backend+UI** |
| Manual notify / lifecycle emails | ❌ (welcome only) | ❌ | **backend+UI** |
| Queryable admin audit log | ❌ (Serilog + 2 matrix only) | partial | **persistence+endpoint+UI** |
| Suspension reason | ❌ (bool only) | — | **field** |

**Dominant gap for the monetization use case:** an **admin operating surface over
the existing Stripe billing** + **plan-price editing** + **lifecycle
notifications** — not building billing from scratch.

---

## Notes on method & freshness

- This is a **point-in-time** snapshot (2026-06-07). Re-verify file:line refs and
  "does not exist" claims before acting on them in a later PR — the codebase moves.
- "Does not exist" means no code was found in an exhaustive search at audit time,
  not that it is impossible.
- SaaS-billing (tenant → operator, Stripe) is kept strictly distinct from
  POS-invoicing (tenant → end customer, CFDI/Facturapi) and POS order payments
  (end customer → tenant, Clip/MercadoPago). Do not conflate the three.
