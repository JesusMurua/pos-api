# BDD-015 — Settings Matrix Enforcement & Contract Cleanup
**Fase:** 20 | **Estado:** Proposed | **Fecha:** 2026-04-20
**Documentos relacionados:**
- [AUDIT-016-backend-schema-reality.md](AUDIT-016-backend-schema-reality.md) — Schema reality check.
- [BDD-013-branch-timezone-and-reports.md](BDD-013-branch-timezone-and-reports.md) — Introduced `Branch.TimeZoneId` but never extended `BranchConfigDto` to surface it.
- [BDD-014-device-security-and-management.md](BDD-014-device-security-and-management.md) — Precedent for per-feature gating at the MVC layer.

---

## 1. Executive Summary

### 1.1 Problem Statement

Only **one** update endpoint across the entire settings surface (`POST /api/branch` → `[RequiresFeature(MultiBranch)]`) consults the Plan × Giros matrix. Seven other endpoints that mutate feature-gated state — `PATCH /api/branch/{id}/settings` (flipping `HasKitchen` / `HasTables`), `PUT /api/business/fiscal` (toggling `InvoicingEnabled`), `PUT /api/branch-payment-config`, `PUT /api/branch/{branchId}/delivery-config`, `PUT /api/branch/{id}` (overloaded with kitchen/tables toggles), `POST /api/branch/folio-config`, and implicitly `PUT /api/business/giro` (changing macro category) — accept any value regardless of plan. A Free-plan caller can flip `HasKitchen = true` via Postman, which is silently persisted; the next time a print job is attempted, the `[RequiresFeature(KdsBasic)]` on `PrintJobController` returns 402, leaving the business in a self-contradictory state ("UI says kitchen is on, backend refuses kitchen operations"). Additionally, `BranchConfigDto` never surfaced the `TimeZoneId` column added by [BDD-013](BDD-013-branch-timezone-and-reports.md), `UpdateConfigRequest` is an ambiguous DTO reused by two endpoints with incompatible semantics, `UpdateBusinessGiroRequest.BusinessTypeIds` carries post-refactor legacy nomenclature, and `CreateBusinessRequest` accepts a bare `{ Name, PlanTypeId }` payload that bypasses the `PrimaryMacroCategoryId` invariant enforced at `/api/auth/register`.

### 1.2 Proposed Solution

Add `[RequiresFeature(...)]` attributes to every unprotected settings endpoint. For endpoints with multi-flag bodies (e.g. `HasKitchen` + `HasTables` on the same PATCH), push the gating down into the service layer so each boolean can be validated against its own feature key independently. Introduce three missing feature keys — `TableService`, `ProviderPayments`, `DeliveryPlatforms` — with stable numeric ids, seeded into the feature-gating matrix with sane defaults per plan × giro. Surface `TimeZoneId` on `BranchConfigDto`. Split the ambiguous `UpdateConfigRequest` into two purpose-built DTOs. Rename `BusinessTypeIds → SubGiroIds` on the giro DTO. Harden `CreateBusinessRequest` by making `PrimaryMacroCategoryId` required (or outline deprecation if `/register` is the only admitted path to `Business` creation).

### 1.3 Expected Outcome / Impact

- **No orphan settings state.** Flipping a feature-gated flag on a plan that does not include the feature returns `402 Payment Required` before anything is persisted; DB and UI stay coherent.
- **Matrix-driven UI becomes tractable.** Frontend can render settings from `GET /api/business/features` and trust that each toggle submission is symmetrically enforced server-side. No "looks enabled, silently fails later" paths.
- **Contract hygiene.** DTOs reflect post-refactor domain language; `BranchConfigDto` closes the [BDD-013](BDD-013-branch-timezone-and-reports.md) loop; `UpdateConfigRequest` ambiguity is removed.
- **Audit surface clean.** Every endpoint that mutates feature-gated state has a documented gate, enabling a single-grep future review: "which endpoints can enable KDS?" → every call site to `FeatureKey.KdsBasic`.

---

## 2. Current State Analysis

### 2.1 Inventory of Unprotected Settings Endpoints

| # | Method | Route | Field(s) mutated | Feature needed | Current gate |
|---|---|---|---|---|---|
| 1 | PATCH | `/api/branch/{id}/settings` | `HasKitchen` | `KdsBasic` | **none** |
| 2 | PATCH | `/api/branch/{id}/settings` | `HasTables` | `TableService` *(to create)* | **none** |
| 3 | PUT | `/api/business/fiscal` | `InvoicingEnabled=true` | `CfdiInvoicing` | **none** |
| 4 | PUT | `/api/branch-payment-config/…` | all payment provider fields | `ProviderPayments` *(to create)* | **none** |
| 5 | POST | `/api/branch-payment-config` | creation path | `ProviderPayments` *(to create)* | **none** |
| 6 | PUT | `/api/branch/{branchId}/delivery-config` | all delivery fields | `DeliveryPlatforms` *(to create)* | **none** |
| 7 | PUT | `/api/branch/{id}` | Name, LocationName, `HasKitchen`, `HasTables` | `KdsBasic` / `TableService` for the flags | **none** |
| 8 | POST | `/api/branch/folio-config` | `FolioPrefix`, `FolioFormat` | `CustomFolios` (existing) | **none** |
| 9 | PUT | `/api/business/giro` | `PrimaryMacroCategoryId`, `BusinessTypeIds` | *none — macro change is a plan-and-giro rewire, not a feature* | **none** |

Row 9 is included for completeness but **is not in scope for feature-gating**. Changing macro is legitimate; the downstream effect (that features enabled by the old macro but not the new one become unavailable) is expected behaviour and already handled by the reactive resolution of `IFeatureGateService`.

### 2.2 Missing `FeatureKey` Entries

The current enum ([POS.Domain/Enums/FeatureKey.cs](../POS.Domain/Enums/FeatureKey.cs)) has 30+ keys but **lacks three** that the audit expects to exist:

| Key | Status | Nearest existing | Design decision |
|---|---|---|---|
| `TableService` | **Missing** | `TableMap = 40`, `WaiterApp = 41` | New key — represents the binary "this branch operates with a table-service model (seated orders, HasTables=true)". Distinct from `TableMap` (visual floor plan UI) and `WaiterApp` (dedicated mobile app). |
| `ProviderPayments` | **Missing** | — | New key — represents "can configure external payment providers (Clip, MercadoPago) for automated intent+webhook flows". Pure manual-cash payments remain universal. |
| `DeliveryPlatforms` | **Missing** | — | New key — represents "can integrate external delivery platforms (UberEats, Rappi, DidiFood) with webhook ingestion". Manual delivery recording stays universal. |

### 2.3 DTO Ambiguities and Drift

| DTO | Problem |
|---|---|
| [`UpdateConfigRequest`](../POS.API/Models/BranchRequests.cs) | Used simultaneously by `PUT /api/branch/{id}` (admin-wide update, uses `HasKitchen`/`HasTables`) and `PUT /api/branch/{id}/config` (runtime-style: uses only `Name`/`LocationName`). Same type, two incompatible contracts. |
| [`BranchConfigDto`](../POS.Domain/PartialModels/BranchConfigDto.cs) | No `TimeZoneId` field despite [BDD-013](BDD-013-branch-timezone-and-reports.md) promoting it to a first-class persistent column. Frontend cannot render timezone-aware UI without a second `/api/branch/{id}` call. |
| [`UpdateBusinessGiroRequest.BusinessTypeIds`](../POS.API/Controllers/BusinessController.cs) | Pre-refactor nomenclature — `BusinessTypeCatalog.Id` in the database is now semantically a sub-giro, not a "business type". Frontend readers conflate this with `PrimaryMacroCategoryId`. |
| [`CreateBusinessRequest`](../POS.API/Models/BranchRequests.cs) | Accepts only `{ Name, PlanTypeId }`. The NOT NULL column `Business.PrimaryMacroCategoryId` gets the default `Retail` silently. `/api/auth/register` correctly requires it; this sibling endpoint is the only way to land a business without a macro choice. |

### 2.4 Why Client-Only Gating Is Not Enough

[`GET /api/business/features`](../POS.API/Controllers/BusinessController.cs) already returns the enabled feature list; the Back Office uses it to hide or disable controls up-front. **This does not replace server enforcement** for two reasons:

1. Any caller with a valid JWT can bypass the UI and POST directly (Postman, curl, browser console).
2. Stale UI state (features changed after login) means the client view may temporarily disagree with the server's current matrix; only the server has the authoritative resolution.

---

## 3. Technical Requirements

### 3.1 Functional Requirements

| ID | Requirement | Acceptance Criteria |
|---|---|---|
| **FR-001** | Add `TableService`, `ProviderPayments`, `DeliveryPlatforms` to `FeatureKey` enum | Numeric ids stable, non-colliding; grouped in domain-appropriate comment sections. |
| **FR-002** | Seed the new feature keys into the Plan × BusinessType matrix | Migration inserts defaults so current businesses do not regress. Free plan denies all three by default; Basic enables `TableService` for restaurant-type macros only; Pro/Enterprise enable all three for every compatible macro. |
| **FR-003** | `PATCH /api/branch/{id}/settings` enforces per-flag gates | When `HasKitchen = true` and `KdsBasic` is not enabled for the caller's business → 402. When `HasTables = true` and `TableService` is not enabled → 402. Disabling either (`false`) requires no gate — revocation is always allowed. Both checks happen before any DB write; partial updates never persist. |
| **FR-004** | `PUT /api/business/fiscal` enforces `CfdiInvoicing` when enabling | `InvoicingEnabled: false → true` transition requires `CfdiInvoicing`. `true → false` (disable) is always allowed. RFC/legalName/taxRegime updates without the enable transition do not require the gate. |
| **FR-005** | `PUT /api/branch-payment-config` and `POST /api/branch-payment-config` require `ProviderPayments` | Class-level `[RequiresFeature(ProviderPayments)]` on the controller — every write method inherits. The GET remains open. |
| **FR-006** | `PUT /api/branch/{branchId}/delivery-config` and `DELETE` require `DeliveryPlatforms` | Same pattern: class-level `[RequiresFeature(DeliveryPlatforms)]` on mutation-only methods (GET stays open). |
| **FR-007** | `PUT /api/branch/{id}` (legacy overload) enforces KDS/Tables gates | Same per-flag semantics as FR-003. Additionally, the obsolete `HasKitchen`/`HasTables` fields on `UpdateConfigRequest` are moved into a new `UpdateBranchRequest` DTO. |
| **FR-008** | `BranchConfigDto` surfaces `TimeZoneId` | Field added; `BranchService` projection (or materialized map) fills it. Backward compatible — additive. |
| **FR-009** | `UpdateConfigRequest` is split into two purpose-built DTOs | `UpdateBranchRequest { Name, LocationName, HasKitchen, HasTables }` for admin PUT; `UpdateBranchConfigRequest { Name, LocationName }` for runtime `/config` PUT. |
| **FR-010** | `BusinessTypeIds` renamed to `SubGiroIds` | DTO property rename; controller parameter rename; frontend contract updated in tandem. JSON property name changes (coordinated with frontend). |
| **FR-011** | `CreateBusinessRequest` either requires `PrimaryMacroCategoryId` or is removed | Preferred: `[Required]` `int PrimaryMacroCategoryId { get; set; }` with `[Range(1,4)]`. Alternative: delete the endpoint if no production caller exists and enforce `/register` as the sole creation path. |

### 3.2 Non-Functional Requirements

- **No data migration for existing rows.** All current `Business` and `Branch` records remain valid; flags already set (e.g. `HasKitchen=true` on a Free-plan business from a pre-BDD-015 write) are not automatically revoked. The enforcement applies to future writes only. A separate cleanup sweep is out of scope.
- **Performance:** `[RequiresFeature]` already caches the matrix resolution inside `IFeatureGateService`; adding it to more endpoints does not materially change P95 latency (< 10 ms extra on cold cache, negligible on warm).
- **Backward compatibility:**
  - The three new `FeatureKey` enum values must be inserted **without renumbering** existing entries. Use ids 42+, 63+, 83+ ranges or pick the next free number within the appropriate section.
  - `TimeZoneId` on `BranchConfigDto` is additive; clients ignoring the field still work.
  - Renaming `BusinessTypeIds → SubGiroIds` is a breaking change for existing frontend callers. Coordinate via frontend PR, or provide a short deprecation window where the DTO accepts both names. See §13 for the decision.
- **Security:** Every `[RequiresFeature]` addition responds with **402 Payment Required** (existing filter contract) — not 403, to preserve the semantic that this is a plan limit, not a permission issue.

---

## 4. Architecture Design

### 4.1 Component Overview

| Component | Change |
|---|---|
| `POS.Domain/Enums/FeatureKey.cs` | **Modified.** Add `TableService`, `ProviderPayments`, `DeliveryPlatforms` with stable ids. |
| `POS.Repository/Migrations/{new}_SeedNewFeatureKeys` | **New.** Seed rows in `FeatureCatalogs` for the 3 new keys, plus default `PlanFeatureMatrix` rows (every plan × feature). Optionally seed `BusinessTypeFeatures` overrides for macros where the default should differ. |
| `POS.API/Controllers/BranchController.cs` | **Modified.** `PATCH /settings` delegates gate checks to `IBranchService`. `PUT /{id}` uses new `UpdateBranchRequest`. |
| `POS.API/Controllers/BusinessController.cs` | **Modified.** `PUT /fiscal` delegates `CfdiInvoicing` check to `IBusinessService.UpdateFiscalConfigAsync`. |
| `POS.API/Controllers/BranchPaymentConfigController.cs` | **Modified.** Class-level `[RequiresFeature(ProviderPayments)]`. |
| `POS.API/Controllers/BranchDeliveryConfigController.cs` | **Modified.** Class-level `[RequiresFeature(DeliveryPlatforms)]`. |
| `POS.Services/IService/IBranchService.cs` | **Modified.** Signature of the update methods receives the feature-gate service injected so per-flag validation runs service-side. |
| `POS.Services/Service/BranchService.cs` | **Modified.** Implements per-flag gate checks in `UpdateBranchAsync` and `UpdateSettingsAsync`. |
| `POS.Services/IService/IBusinessService.cs` | **Modified.** `UpdateFiscalConfigAsync` gains a conditional `CfdiInvoicing` check. |
| `POS.Services/Service/BusinessService.cs` | **Modified.** Implementation. |
| `POS.API/Models/BranchRequests.cs` | **Modified.** Split `UpdateConfigRequest` into two DTOs. `CreateBusinessRequest` gains `PrimaryMacroCategoryId`. |
| `POS.API/Controllers/BusinessController.cs` | **Modified.** `UpdateBusinessGiroRequest.BusinessTypeIds` renamed to `SubGiroIds`. |
| `POS.Domain/PartialModels/BranchConfigDto.cs` | **Modified.** Add `TimeZoneId`. |
| `POS.Repository/Repository/BranchRepository.cs` | **Modified.** Projection for `BranchConfigDto` maps `TimeZoneId`. |

### 4.2 Enforcement Strategy — Attribute vs. Service-Layer

**Attribute-based (`[RequiresFeature(FeatureKey.X)]`)** — used when a whole method or class maps to a single feature:

- `BranchPaymentConfigController` (class-level) → `ProviderPayments`
- `BranchDeliveryConfigController` (mutations only) → `DeliveryPlatforms`

**Service-layer** — used when a single endpoint toggles multiple feature-gated flags in one payload, and each flag must be judged independently:

- `PATCH /api/branch/{id}/settings` — `HasKitchen` checked against `KdsBasic`, `HasTables` checked against `TableService`. Both are optional within the same request; only the flags being set to `true` require their respective gates.
- `PUT /api/business/fiscal` — the `CfdiInvoicing` check fires only on the `false → true` transition of `InvoicingEnabled`.

The service-layer path uses the existing `IFeatureGateService.EnforceAsync(businessId, featureKey)` which throws `PlanLimitExceededException` — a global exception-middleware maps this to HTTP 402 with the standard payload.

### 4.3 Data Flow — `PATCH /api/branch/{id}/settings` (per-flag gate)

1. Controller receives `{ hasKitchen?, hasTables? }`.
2. `BusinessId` extracted from JWT.
3. `_branchService.UpdateSettingsAsync(branchId, request.HasKitchen, request.HasTables, businessId)` invoked.
4. Service loads branch; validates tenancy (`branch.BusinessId == businessId`, else 404 opaque).
5. For each field present in the request:
   - If setting to `true`, call `_featureGate.EnforceAsync(businessId, matchingFeatureKey)`. On failure the exception propagates → 402 via middleware; nothing is saved.
   - If setting to `false`, apply directly.
6. `SaveChangesAsync`.
7. Return updated `BranchConfigDto` (now including `TimeZoneId`).

### 4.4 Data Flow — `PUT /api/business/fiscal` (transition gate)

1. Service loads current `InvoicingEnabled`.
2. If `request.InvoicingEnabled == true && current == false` → `_featureGate.EnforceAsync(businessId, CfdiInvoicing)`.
3. Apply all fiscal fields (Rfc, TaxRegime, LegalName, InvoicingEnabled) in one save.

### 4.5 Database Schema Changes

**`FeatureCatalogs` table** (seed rows only) — insert three rows for the new feature keys with their canonical codes and display names.

**`PlanFeatureMatrix` table** (seed rows only) — insert default `(planTypeId, featureId)` rows so every plan has a declared default for each new feature. Suggested defaults:

| Feature | Free | Basic | Pro | Enterprise |
|---|---|---|---|---|
| `TableService` | false | true (table-service macros only, via `BusinessTypeFeature` override; false otherwise) | true | true |
| `ProviderPayments` | false | false | true | true |
| `DeliveryPlatforms` | false | false | true | true |

**`BusinessTypeFeature` table** (seed rows only) — overrides where a macro × feature pair deviates from the plan default. Recommended overrides:

- `TableService` enabled for `FoodBeverage` (id=1) and `QuickService` (id=2) across Basic/Pro/Enterprise; disabled for `Retail` (id=3) and `Services` (id=4).
- `DeliveryPlatforms` enabled for `FoodBeverage` / `QuickService` across Pro/Enterprise; disabled for `Retail`/`Services`.

**No column additions.** This BDD only seeds catalog + matrix data; no schema alterations beyond seeds.

**Migration ID:** `AddSettingsFeatureKeys` (or `SeedNewFeatureKeys`). Down-migration deletes the three seed rows from `FeatureCatalogs`, `PlanFeatureMatrix`, and `BusinessTypeFeature`.

---

## 5. API Contract

### 5.1 Changed Endpoint Semantics

| Endpoint | Gate | New response code on plan violation |
|---|---|---|
| `PATCH /api/branch/{id}/settings` | `KdsBasic` (hasKitchen), `TableService` (hasTables) — per flag | 402 |
| `PUT /api/branch/{id}` | Same as above | 402 |
| `PUT /api/business/fiscal` | `CfdiInvoicing` on enable transition | 402 |
| `PUT /api/branch-payment-config` + `POST` | `ProviderPayments` (class-level) | 402 |
| `PUT /api/branch/{branchId}/delivery-config` + `DELETE` | `DeliveryPlatforms` (class-level) | 402 |

**No path or method changes.** Request body shapes unchanged for rows 1-5 except `UpdateConfigRequest` split (§5.3).

### 5.2 Modified DTO — `BranchConfigDto`

| Field | Type | New? | Source |
|---|---|---|---|
| (existing fields) | — | | |
| `TimeZoneId` | `string` | **NEW** | `Branch.TimeZoneId` — non-nullable per [BDD-013](BDD-013-branch-timezone-and-reports.md) |

### 5.3 Split DTOs — `UpdateConfigRequest` → `UpdateBranchRequest` + `UpdateBranchConfigRequest`

**`UpdateBranchRequest`** — for `PUT /api/branch/{id}` (Owner admin wide-update):

| Field | Type | Validation | Notes |
|---|---|---|---|
| `Name` | `string` | `[Required][MaxLength(100)]` | |
| `LocationName` | `string?` | `[MaxLength(200)]` | |
| `HasKitchen` | `bool?` | — | Gated by `KdsBasic` when `true` |
| `HasTables` | `bool?` | — | Gated by `TableService` when `true` |

**`UpdateBranchConfigRequest`** — for `PUT /api/branch/{id}/config` (runtime config):

| Field | Type | Validation |
|---|---|---|
| `Name` | `string` | `[Required][MaxLength(100)]` |
| `LocationName` | `string?` | `[MaxLength(200)]` |

Kitchen/tables flags are **not** accepted on this endpoint — if the UI needs to toggle them, it must call `PATCH /api/branch/{id}/settings`.

### 5.4 Renamed DTO Field — `BusinessTypeIds → SubGiroIds`

Current:
```
UpdateBusinessGiroRequest {
  PrimaryMacroCategoryId: int,
  BusinessTypeIds: int[],            // ← old name
  CustomGiroDescription?: string
}
```

New:
```
UpdateBusinessGiroRequest {
  PrimaryMacroCategoryId: int,
  SubGiroIds: int[],                 // ← renamed
  CustomGiroDescription?: string
}
```

JSON property name changes accordingly (`camelCase: subGiroIds`). See §13 for deprecation-window decision.

### 5.5 Hardened DTO — `CreateBusinessRequest`

| Field | Before | After |
|---|---|---|
| `Name` | `[Required][MaxLength(100)] string` | unchanged |
| `PlanTypeId` | `[Required] int` | unchanged |
| `PrimaryMacroCategoryId` | absent | **`[Required][Range(1,4)] int`** |

Alternative: mark the endpoint `[Obsolete]` and delete once no production caller is confirmed. A one-file grep is sufficient to decide; if `POST /api/business` has no frontend consumer, prefer deletion.

### 5.6 Service Interface Changes

```
IBranchService:
  Task<BranchConfigDto> UpdateBranchAsync(int branchId, UpdateBranchRequest request, int businessId);
  Task<BranchConfigDto> UpdateSettingsAsync(int branchId, bool? hasKitchen, bool? hasTables, int businessId);
  Task<BranchConfigDto> UpdateConfigAsync(int branchId, string name, string? locationName);   // unchanged
```

```
IBusinessService:
  Task<Business> UpdateFiscalConfigAsync(int businessId, UpdateFiscalConfigRequest request);
  // Throws PlanLimitExceededException when toggling InvoicingEnabled from false→true
  // without CfdiInvoicing feature.
```

---

## 6. Business Logic Specifications

### 6.1 Per-Flag Gate Algorithm

```
function UpdateSettingsAsync(branchId, hasKitchen?, hasTables?, callerBusinessId):
    branch := load tenant-scoped by branchId and callerBusinessId
    if branch is null: throw NotFoundException

    if hasKitchen is true:
        await featureGate.EnforceAsync(callerBusinessId, KdsBasic)
    if hasTables is true:
        await featureGate.EnforceAsync(callerBusinessId, TableService)

    if hasKitchen has value: branch.HasKitchen := hasKitchen.Value
    if hasTables  has value: branch.HasTables  := hasTables.Value

    await uow.SaveChangesAsync()
    return projected BranchConfigDto
```

**Key point:** every enforce call runs *before* the first DB write. A request with `{ hasKitchen: true, hasTables: true }` where only `TableService` fails throws on the second enforce and **no** kitchen flag leaks into storage.

### 6.2 Fiscal Transition Gate Algorithm

```
function UpdateFiscalConfigAsync(businessId, request):
    business := load by businessId
    if request.InvoicingEnabled == true and business.InvoicingEnabled == false:
        await featureGate.EnforceAsync(businessId, CfdiInvoicing)
    business.Rfc        := request.Rfc?.Trim().ToUpperInvariant()
    business.TaxRegime  := request.TaxRegime
    business.LegalName  := request.LegalName
    business.InvoicingEnabled := request.InvoicingEnabled
    await uow.SaveChangesAsync()
    return business
```

### 6.3 Validation Rules

| ID | Rule | Error |
|---|---|---|
| **VR-001** | Setting `HasKitchen=true` without `KdsBasic` | `PlanLimitExceededException` → 402 |
| **VR-002** | Setting `HasTables=true` without `TableService` | `PlanLimitExceededException` → 402 |
| **VR-003** | `InvoicingEnabled: false → true` without `CfdiInvoicing` | `PlanLimitExceededException` → 402 |
| **VR-004** | Any mutation on `BranchPaymentConfig*` without `ProviderPayments` | 402 (attribute filter) |
| **VR-005** | Any mutation on `BranchDeliveryConfig*` without `DeliveryPlatforms` | 402 (attribute filter) |
| **VR-006** | `PUT /api/branch/{id}` (via `UpdateBranchRequest`) flipping flags without features | 402 (service-layer) |
| **VR-007** | `CreateBusinessRequest.PrimaryMacroCategoryId ∉ [1,4]` | 400 via `[Range]` |
| **VR-008** | `SubGiroIds` empty or missing on giro update | 400 — existing guard preserved |

### 6.4 Edge Cases

| Case | Behavior |
|---|---|
| Request disables both flags (`hasKitchen: false, hasTables: false`) on a Free-plan branch | Both writes succeed — revocation never requires a feature. |
| `PUT /api/business/fiscal` with `InvoicingEnabled` unchanged (true→true or false→false) | No gate check; other fiscal fields (RFC, LegalName) update freely. |
| Legacy branch with `HasKitchen=true` persisted before BDD-015 on a Free plan | Left as-is; no cleanup sweep. Next write (e.g. a `PATCH /settings` with `{ hasKitchen: true }`) would 402, but setting `{ hasKitchen: false }` always succeeds, allowing operators to clear the inconsistent state. |
| Admin changes macro (`PUT /api/business/giro`) to one that makes `TableService` plan-unavailable | Existing flag stays `true`; next write attempting to re-enable it 402s. Documented; not auto-revoked. |
| Frontend is stale (features changed after login) | Server returns 402 with the new plan snapshot in the payload; frontend refetches `/features`. |

---

## 7. Performance Optimization Strategy

### 7.1 Gate Overhead

`IFeatureGateService.EnforceAsync` already caches the Plan × BusinessType matrix resolution at the service level (established pattern, unchanged here). Adding ~5 more call sites adds constant ms on cold cache, negligible on warm. No additional indexes needed.

### 7.2 Projection for `BranchConfigDto`

`TimeZoneId` is a single extra column in the existing projection; zero join impact. The repository's `GetBranchConfigDtoAsync` already fetches the Branch row — adding one field is free.

---

## 8. Error Handling Strategy

| Scenario | Exception | HTTP | Body |
|---|---|---|---|
| Plan does not include requested feature | `PlanLimitExceededException` | 402 | `{ error, message, feature, currentPlan, statusCode }` (existing filter contract) |
| Branch not found / cross-tenant | `NotFoundException` | 404 | opaque |
| Invalid body (model-binding) | `ValidationException` | 400 | default |
| Missing `PrimaryMacroCategoryId` on `CreateBusinessRequest` | `[Required]` | 400 | model-state |

No new exception types.

---

## 9. Testing Requirements

### 9.1 Unit Tests

- `UpdateSettingsAsync(hasKitchen=true)` with `KdsBasic` disabled throws `PlanLimitExceededException`; nothing persisted.
- `UpdateSettingsAsync(hasKitchen=true)` with `KdsBasic` enabled succeeds.
- `UpdateSettingsAsync(hasKitchen=true, hasTables=true)` with only `TableService` disabled: throws before any DB write; the kitchen flag does not persist.
- `UpdateSettingsAsync(hasKitchen=false, hasTables=false)` on Free plan: always succeeds.
- `UpdateFiscalConfigAsync` with `InvoicingEnabled: false → true` on Basic plan (no `CfdiInvoicing`): throws 402.
- `UpdateFiscalConfigAsync` with `InvoicingEnabled: true → true` on Basic plan: succeeds.
- `CreateBusinessRequest` without `PrimaryMacroCategoryId`: 400 model-state error.

### 9.2 Integration Tests

- `PATCH /api/branch/{id}/settings` with `{ hasKitchen: true }` as Free-plan Owner → 402 with matching `feature: "KdsBasic"` in payload.
- `PUT /api/business/fiscal` enabling invoicing on Basic plan → 402 `feature: "CfdiInvoicing"`.
- `POST /api/branch-payment-config` on Basic plan → 402 `feature: "ProviderPayments"`.
- `PUT /api/branch/{id}/delivery-config` on Basic plan → 402 `feature: "DeliveryPlatforms"`.
- `GET /api/branch/{id}/config` now returns `TimeZoneId`.
- Legacy frontend sending `{ businessTypeIds: [...] }` → 400 (fields unknown) **unless** deprecation window is active (see §13).

### 9.3 Performance

- Per-endpoint P95 overhead < 5 ms additional on warm matrix cache.

---

## 10. Implementation Phases

| Phase | Scope | Dependencies | Complexity |
|---|---|---|---|
| **Phase 1** | Add 3 `FeatureKey` enum members. Build migration seeding `FeatureCatalogs` + `PlanFeatureMatrix` defaults + `BusinessTypeFeature` overrides (§4.5). Apply locally. | None | **Low** |
| **Phase 2** | Add `[RequiresFeature(ProviderPayments)]` to `BranchPaymentConfigController`; `[RequiresFeature(DeliveryPlatforms)]` to `BranchDeliveryConfigController`. | Phase 1 | **Low** |
| **Phase 3** | Service-layer gating: `BranchService.UpdateSettingsAsync` per-flag; wire `PATCH /settings` to pass `businessId`. | Phase 1 | **Medium** |
| **Phase 4** | Service-layer gating: `BusinessService.UpdateFiscalConfigAsync` transition gate; refactor controller to call through the service. | Phase 1 | **Low** |
| **Phase 5** | Split `UpdateConfigRequest` → `UpdateBranchRequest` + `UpdateBranchConfigRequest`. Update both endpoints and service methods. Apply service-layer gates to `PUT /api/branch/{id}`. | Phases 2-3 | **Medium** |
| **Phase 6** | Add `TimeZoneId` to `BranchConfigDto`; update repository projection. | None (parallel) | **Low** |
| **Phase 7** | Rename `BusinessTypeIds → SubGiroIds` in `UpdateBusinessGiroRequest`. Coordinate frontend PR. | Frontend merge ready | **Low** |
| **Phase 8** | Harden `CreateBusinessRequest` with `PrimaryMacroCategoryId [Required] [Range(1,4)]` — or delete the endpoint if no consumer. | None (parallel) | **Low** |

Phases 1 → 2/3/4/5 are chained (the matrix seed is a prerequisite to any enforce call). Phases 6, 7, 8 are independent and may ship in parallel.

---

## 11. Out of Scope

- **Cleanup of legacy corrupted flags.** A branch with `HasKitchen=true` on a Free plan persists; no automatic rectification. Operators may flip it to `false` manually; the UI can surface a banner "this setting is active but not included in your plan" if desired (frontend work).
- **Per-user permission gating.** Role-based authorization (Owner/Manager) remains orthogonal to feature gating. A Manager on a plan with `KdsBasic` can still toggle the flag if their role allows.
- **Audit log of who changed what feature flag.** Not in scope; would need an `AuditLog` entity not covered by any current BDD.
- **`PUT /api/business/giro` feature-coherence checks.** Changing macro is allowed even if the new macro would make some existing flags plan-unavailable. Documented edge case (§6.4).
- **Frontend changes beyond DTO contract coordination.** Form revamp, matrix-driven rendering, stale-state reconciliation — those are frontend BDDs.
- **New endpoints.** This BDD only hardens existing ones; no `GET /api/settings` aggregate endpoint, no `PATCH /api/business`.

---

## 12. Security Considerations

- **No 403 where 402 is correct.** A plan limit is not a permission issue. The existing `RequiresFeatureAttribute` and `PlanLimitExceededException` already map to 402 — this invariant must not leak into 403s in the new enforcement paths.
- **Tenant guard preserved.** Every service-layer path still validates `branch.BusinessId == callerBusinessId` (opaque 404 on mismatch). Feature gate is *additive*, not a substitute.
- **No information disclosure.** 402 payloads include the feature name and current plan — already present in existing filter. No new PII surface.
- **Revocation is always allowed.** Disabling a flag never requires a feature check — operators can always clean state, even if they cannot re-enable it.

---

## 13. Risk & Migration Notes

### 13.1 Rename breaking change — `BusinessTypeIds → SubGiroIds`

Two viable strategies:

**Strategy A — hard rename, single release.** Coordinate frontend and backend PRs in one release. Clean, no legacy compat shim.

**Strategy B — deprecation window.** Add `[JsonPropertyName("subGiroIds")]` on the new `SubGiroIds` field, and keep a temporary `[Obsolete] public List<int> BusinessTypeIds { get => SubGiroIds; set => SubGiroIds = value; }` shim for 1–2 releases. Remove after frontend confirms migration.

**Recommendation:** Strategy A if frontend and backend ship together; Strategy B if deployments are decoupled. Pick at implementation time with the frontend team.

### 13.2 Matrix seed order

The new `FeatureKey` enum members must exist **before** the seed migration runs. The single-file migration inserting `FeatureCatalogs` rows references the enum id directly via integer literals; no compile-time FK to `FeatureKey`. Safe to ship in one migration after the enum change.

### 13.3 Existing flag state

No backfill modifies existing rows. Legacy settings stay as they are; new writes enforce the matrix. This is intentional to avoid a "BDD-015 breaks my customer's setup" surprise. If cleanup is desired, ship it in a follow-up BDD with explicit customer comms.

### 13.4 Rollback strategy

If a 402 starts firing erroneously in production (e.g. seed matrix misconfigured), each `[RequiresFeature]` attribute can be hot-removed in a targeted patch; service-layer calls can be feature-flagged to no-op. The Down migration restores the pre-BDD seed state.
