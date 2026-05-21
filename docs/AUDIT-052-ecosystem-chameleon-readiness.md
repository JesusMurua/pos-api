# AUDIT-052 — Ecosystem-wide Chameleon Readiness (Cloud + Frontend + Bridge)

**Date:** 2026-05-21
**Scope:** Systemic readiness of the 3-component ecosystem — `pos-api` (.NET 10 cloud), `restaurant-app` (Angular 18 admin/POS frontend), and `pos-local-bridge` (Windows headless C# service) — for total domain flexibility, long-term scalability, and automated testing across MacroCategory / Giro / Subgiro variance.
**Status:** Read-only systemic audit across the 3 repos. No code modified.
**Related:** [AUDIT-024](AUDIT-024-chameleon-domain-readiness.md) (backend domain shape), [BDD-019](BDD-019-chameleon-domain-readiness.md) (chameleon design), [BDD-020](BDD-020-chameleon-metadata-architecture.md) (metadata architecture).

---

## 1. Executive Summary

The platform's **declarative metadata layer is excellent** — `FeatureGateService` evaluates a 3-D matrix (Plan × MacroCategory × Feature) entirely from DB-driven catalogs ([POS.Services/Service/FeatureGateService.cs:15-189](../POS.Services/Service/FeatureGateService.cs#L15-L189)), the frontend's `GIRO_FEATURE_MAP` is a declarative `Record` not a `switch` tree (`restaurant-app:src/app/core/enums/feature-key.enum.ts:157-162`), and routes are vertical-agnostic. **Vector A (domain flexibility) is largely solved.**

However, two systemic risks compound as new verticals onboard:

1. **The local bridge is "always-on" for every supervisor regardless of tenant claims** (`pos-local-bridge:Program.cs:56-63`). A Restaurant tenant that only wants thermal printing spawns TurnstileSupervisor, BiometricSupervisor, AccessSyncSupervisor — wasting resources, polluting logs, and masking real operational signal.
2. **The cloud API enforces multi-tenant isolation by developer discipline, not by EF Core Global Query Filters.** Zero `HasQueryFilter(...)` calls exist in [ApplicationDbContext.cs](../POS.Repository/ApplicationDbContext.cs); every new repository method that omits `.Where(BranchId == ...)` becomes a silent cross-tenant data leak. The `BranchInjectionInterceptor` referenced in defensive comments does not exist on disk.

The third systemic risk is **testability**: the cloud API has **zero test projects**, the frontend has **1 unit spec out of 246 TypeScript files**, and the bridge lacks virtual implementations for 3 of 4 hardware abstractions. Without a `JwtTestFactory` primitive on the backend and a JWT-mock fixture on the frontend, boundary conditions across verticals cannot be exercised before they hit production.

This audit catalogues findings only — fixes are out of scope per the read-only directive.

---

## 2. Vector A — Domain Flexibility / Zero-Hardcoding

### 2.1 pos-api — PASS

The feature gating system is fully DB-driven via the 3-D matrix:

- [FeatureKey enum (POS.Domain/Enums/FeatureKey.cs:1-65)](../POS.Domain/Enums/FeatureKey.cs#L1-L65) — stable numeric IDs, with range conventions (e.g. `120-129` reserved for Access Control).
- [FeatureGateService.cs:15-189](../POS.Services/Service/FeatureGateService.cs#L15-L189) — evaluates per request:
  - `PlanFeatureMatrix` (Plan-to-feature bindings)
  - `BusinessTypeFeature` (MacroCategory-to-feature applicability)
  - `PlanBusinessTypeFeatureOverride` (per-combo overrides)
  - `FeatureCatalog` (feature metadata)
- Per-business cache with 5-min TTL ([FeatureGateService.cs:20, 87-91](../POS.Services/Service/FeatureGateService.cs#L20)).

Vertical-specific behavior is encoded as **flags on `MacroCategory`**, not as string checks ([POS.Domain/Models/Catalogs/MacroCategory.cs:31-34](../POS.Domain/Models/Catalogs/MacroCategory.cs#L31-L34)):

- `HasKitchen` (bool), `HasTables` (bool) — hydrated by `BranchService.CreateAsync()` from `Business.PrimaryMacroCategoryId` ([BranchService.cs:56-84](../POS.Services/Service/BranchService.cs#L56-L84)).
- `BranchService` deliberately **overwrites caller-supplied flags** with macro defaults (lines 50-51, 66-67) — defensive against malicious/outdated clients trying to force restaurant capabilities onto a gym branch.

Controllers gate via `[RequiresFeature(FeatureKey.X)]` ([POS.API/Filters/RequiresFeatureAttribute.cs](../POS.API/Filters/RequiresFeatureAttribute.cs)), e.g. `AccessControlController:16` uses `[RequiresFeature(FeatureKey.RealtimeAccessControl)]` — no hardcoded "this is for gyms".

**Zero instances** of `if/switch` on giro strings, hardcoded "Restaurant"/"Gym"/"Hotel"/"Pharmacy"/"Bar", or hardcoded MacroCategory.InternalCode checks.

### 2.2 restaurant-app — PASS (with scoped UX copy debt)

The frontend mirrors the backend's declarative approach:

- `MacroCategoryType` enum (4 values: FoodBeverage, QuickService, Retail, Services) — single source of truth at `restaurant-app:src/app/core/enums/config.enum.ts:55-60`.
- `extractMacroCategoryFromJwt()` at `restaurant-app:src/app/core/utils/jwt.utils.ts:79-88` — fault-tolerant normalization (case/punctuation) before mapping to the enum.
- `GIRO_FEATURE_MAP: Record<MacroCategoryType, FeatureKey[]>` at `restaurant-app:src/app/core/enums/feature-key.enum.ts:157-162` — declarative, no switch chains.
- `TenantContextService.isApplicableToGiro(feature)` at `restaurant-app:src/app/core/services/tenant-context.service.ts:182-191` — single decision point for hide-vs-lock UI logic.
- Routes are vertical-agnostic: `/pos`, `/kiosk`, `/kitchen`, `/reception`, `/tables` (`restaurant-app:src/app/app.routes.ts`). No `/restaurant/...` or `/gym/...` patterns.
- `posEntryGuard` resolves the correct POS route variant via `DeviceRoutingService.resolvePosRoute()` based on the `posExperience` signal — not a giro string.

**Scoped hardcoding remains in UX copy only:**

- Product form placeholder text uses a `switch` on 4 `PosExperience` values with macro fallback (`restaurant-app:src/app/modules/admin/components/products/product-form/product-form.component.ts:175-217`). Cosmetic, not behavioral.
- Single `if` for "Pantallas y Accesos" sidebar label at `restaurant-app:src/app/modules/admin/admin-shell.component.ts:158-161` (checks `currentSubCategory === Gym || currentMacro === Services`). Branded text mixed with logic.

### 2.3 pos-local-bridge — N/A (no domain logic; pure hardware proxy)

---

## 3. Vector B — Architectural Extensibility

### 3.1 pos-api — WEAK (multi-tenant isolation by discipline)

Verified across [ApplicationDbContext.cs](../POS.Repository/ApplicationDbContext.cs) `OnModelCreating` (lines 156-1670):

- **Zero `HasQueryFilter(...)` declarations.** EF Core is NOT configured to auto-prepend `WHERE BusinessId = X OR BranchId = X` to queries.
- Base [GenericRepository.cs:7-48](../POS.Repository/GenericRepository.cs#L7-L48) passes the filter directly to `_dbSet.Where(filter)` with no tenant pre-filter.
- Every repository must manually inject the tenant predicate. Spot-checks:
  - `OrderRepository.GetByBranchAndDateAsync()` — explicit `.Where(o => o.BranchId == branchId)`.
  - `ProductRepository.GetByBarcodeAsync()` — explicit `.Where(p => p.BranchId == branchId && ...)`.
  - `AccessControlService.EvaluateQrAccessAsync()` ([AccessControlService.cs:67-68](../POS.Services/Service/AccessControlService.cs#L67-L68)) — explicit `c.BusinessId == callerBusinessId && c.QrToken == hashedToken`.

**No interceptor or middleware** detected to inject claims context into the DbContext. The `BranchInjectionInterceptor` referenced in defensive comments (e.g. [AccessControlService.cs:139-141](../POS.Services/Service/AccessControlService.cs#L139-L141)) **does not exist on disk**. The defensive `BranchId = callerBranchId` assignment in those services is the only safety net.

**Design consequence:** If a developer writes a repository method without the tenant predicate, the method silently leaks cross-tenant data. The surface area for omission grows with each new vertical onboarded.

### 3.2 restaurant-app — GOOD (polymorphic, except form structure)

- **Navigation sidebar** ([`admin-shell.component.ts:119-138`](file:///d:/Source/restaurant-app/src/app/modules/admin/admin-shell.component.ts)) is a static array of `NavItem` objects with optional `feature` and `subCategory` gates; the template uses `@if (isHiddenBySubCategory(item))` and a structural `*appFeature` directive (`restaurant-app:src/app/shared/directives/app-feature.directive.ts:41-135`) with `hide`/`lock` modes.
- **POS layout** uses a "Chameleon" pattern via `UnifiedPosComponent` (`restaurant-app:src/app/modules/pos/components/unified-pos/unified-pos.component.ts:38-99`) — swaps view mode (`grid` ↔ `keypad`) driven by signals + `PosViewModeService.initializeDefault()` (line 53-58) which selects 'keypad' for Services/Quick and 'grid' for others. User overrides persist in localStorage.
- **Device routing** uses declarative map `MACRO_POS_EXPERIENCE: Record<MacroCategoryType, PosExperience>` (`restaurant-app:src/app/core/services/device-routing.service.ts:16-21`).

**Limitation — forms are static templates:**

- `ReactiveFormsModule` with FormBuilder, not schema-driven from the API.
- Membership fields (`isMembership`, `membershipDurationDays`) hardcoded with `currentMacro === MacroCategoryType.Services` check (`product-form.component.ts:165-167`).
- Adding Pharmacy (controlled substances, prescription required) or Hotel (room type, occupancy limit, check-in policy) requires template edits, not API config.

### 3.3 pos-local-bridge — CRITICAL (all supervisors always-on)

**All 7 background workers instantiated unconditionally at boot** (`pos-local-bridge:PosLocalBridge.Host/Program.cs:56-63`):

| Worker | File | Always-on? | Reads JWT claims? |
|---|---|---|---|
| `ResetBootstrapService` | `PosLocalBridge.Security/ResetBootstrapService.cs:34-55` | Yes | No |
| `TurnstileSupervisor` | `PosLocalBridge.Host/TurnstileSupervisor.cs:24-39` | Yes | No |
| `PrinterSupervisor` | `PosLocalBridge.Host/PrinterSupervisor.cs:26-38` | Yes | No |
| `BiometricSupervisor` | `PosLocalBridge.Host/BiometricSupervisor.cs:28-52` | Yes | No |
| `SerialInputSupervisor` | `PosLocalBridge.Host/SerialInputSupervisor.cs:48-106` | Yes | No (uses local config) |
| `AccessSyncSupervisor` | `PosLocalBridge.Host/AccessSyncSupervisor.cs:33-58` | Yes | No |
| `HealthMonitor` | `PosLocalBridge.Host/HealthMonitor.cs:43-92` | Yes | No (hardcodes `const int biometrics = 1` at line 86) |

The JWT received during pairing carries `branchId`, `businessId`, `mode`, `features` — but **none is ever decoded**. `PairingResponse` (`pos-local-bridge:PosLocalBridge.Contracts/Security/PairingResponse.cs:5-13`) only logs the claims (`HttpPairingService:80`) for operator visibility.

No imports of `System.IdentityModel.Tokens.Jwt`, `JwtSecurityTokenHandler`, or any JWT decoding logic found anywhere in the bridge. The CLAUDE.md of the bridge (line 26) explicitly marks `IDeviceClaimsReader` as a future TODO.

**Consequence:** A Restaurant tenant that only needs thermal printing still spawns 6 supervisors that wait idly for events that never arrive. A Gym tenant on a degraded plan still receives the full sync payload. Resource waste + log noise grows linearly per vertical.

`FinoCloudClient` performs SignalR token injection via `AccessTokenProvider` (`pos-local-bridge:PosLocalBridge.Transport/Cloud/FinoCloudClient.cs:50`) but never inspects the JWT.

Hardware connection failures are caught and logged gracefully (Rule #1 of bridge CLAUDE.md), but **graceful error handling is not the same as claim-driven gating** — a supervisor stays active and consumes resources even when its hardware will never be present.

---

## 4. Vector C — Testability

### 4.1 pos-api — FAIL (zero test projects)

- Glob over `**/*.Test.csproj` and `**/*Tests*` → **no results**.
- No `Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>` references anywhere.
- No JWT factory helper for synthetic device/user tokens.
- No integration tests asserting tenant isolation (e.g. "User from BusinessA cannot read Order from BusinessB").
- No tests verifying feature-gate enforcement (e.g. "Gym tenant cannot POST to `/api/Table` even with valid JWT").

**Critical primitive missing:**

```text
JwtTestFactory.CreateDeviceToken(
    businessId: 1,
    branchId: 2,
    mode: "bridge",
    features: ["RealtimeAccessControl", "ThermalPrint"])
```

Without this helper, NO test of boundary condition across the (Plan × Macro × Feature) matrix is practical.

### 4.2 restaurant-app — SPARSE (1 unit spec out of 246 TS files)

- Single dedicated spec: `restaurant-app:src/app/core/services/cash-register.service.spec.ts` (covers promise caching race + cold-boot register linking).
- **No tests for** `TenantContextService`, `extractMacroCategoryFromJwt`, `GIRO_FEATURE_MAP` applicability, or `*appFeature` directive behavior.
- E2E uses Playwright (`restaurant-app:playwright.config.ts`) with 5 specs in `e2e/tests/` (login, PIN, stock-receipts, smoke). Page Object pattern present (`BasePage`, `LoginPage`, `PinPage`, `StockReceiptsPage`).
- Device config fixture exists (`restaurant-app:e2e/fixtures/device-config.ts` with `seedDeviceConfig()`) but **no equivalent JWT mock fixture**. Existing E2E hits the real backend with live credentials (`'test@example.com'`).
- Zero multi-vertical scenarios. Zero snapshot/visual regression tests per giro.

### 4.3 pos-local-bridge — PARTIAL (unit OK, E2E absent)

**Hardware abstractions are clean** — every hardware concern lives behind an interface, mockable via NSubstitute:

- `IBiometricScanner`, `IPrinterRouter`, `ITurnstileController`, `ISerialInputScanner`, `IAccessGate`, `IAccessCache`, `ICloudClient` — all interface-based.

**Unit tests present** (NSubstitute-driven):

- `BiometricSupervisorTests` (`pos-local-bridge:PosLocalBridge.Tests/Host/BiometricSupervisorTests.cs:1-77`) — event subscription, handler invocation, cancellation.
- `HttpPairingServiceTests` (`pos-local-bridge:PosLocalBridge.Tests/Security/HttpPairingServiceTests.cs:1-120`) — 200/401/empty-code paths.
- Plus: `EscPosRouterTests`, `FileTokenStoreTests`, `SqliteAccessCacheTests`, `InfiniteRetryPolicyTests`, `ResetBootstrapServiceTests`.

**Virtual implementations exist only for biometric:**

- `VirtualBiometricScanner` (`pos-local-bridge:PosLocalBridge.Hardware.Biometrics/VirtualBiometricScanner.cs:1-107`) — emits dummy Base64 templates on a configurable interval. Wired as the **production** `IBiometricScanner` (`Program.cs:48`).
- **No `VirtualTurnstileController`** — only `ComPortTurnstileController`.
- **No `VirtualPrinterRouter`** — only `EscPosRouter` (direct USB/network).
- **No `VirtualSerialInputScanner`** — only `ContinuousSerialScanner` (direct COM).

**End-to-end harness is absent:**

- No mock SignalR hub server. `FinoCloudClient` hardcodes the hub URL from config; no interface to swap it for a test instance.
- No test fixture that emits cloud events (OpenTurnstile, SendEscPosCommand, SyncAccessData) into the bridge and verifies device responses.
- No tests for claim-based supervisor behavior (impossible until claim-gating exists — see §3.3).

Additional reliability gap: `ContinuousSerialScanner.cs:19-20` never sets `ReadTimeout`, so a hung COM port stays silently hung until process shutdown.

---

## 5. Synthesis — Critical Weaknesses (build/runtime risk under new domains)

### CW-1 — Bridge supervisors NOT claim-gated [pos-local-bridge]

**Risk:** Every new vertical onboarded means every bridge instance — regardless of whether the tenant needs that hardware — spawns the full supervisor set. Restaurant bridges run TurnstileSupervisor + BiometricSupervisor + AccessSyncSupervisor unnecessarily. `HealthMonitor` reports a hardcoded `biometrics = 1` for tenants that don't have biometrics. Not a build break, but operational footprint grows linearly with vertical count and masks real signal.

### CW-2 — Multi-tenant data leak risk by developer omission [pos-api]

**Risk:** No EF Global Query Filters. The first repository method that omits `.Where(BranchId == ...)` cross-tenant leaks silently. Each new vertical → more entities → more repositories → more opportunities for omission. Regulatory time bomb (GDPR/LFPDPPP-MX) at scale.

### CW-3 — Frontend forms are static templates [restaurant-app]

**Risk:** Adding new vertical-specific entity fields (Pharmacy: `controlSubstance`, `prescriptionRequired`; Hotel: `roomType`, `occupancyLimit`) requires template-level edits and a frontend deploy. Blocks self-service onboarding of new verticals.

---

## 6. Architectural Debt

### AD-1 — `IDeviceClaimsReader` missing [pos-local-bridge]

Marked TODO in bridge CLAUDE.md:26. PairingResponse claims (`HttpPairingService:80`) are logged but never used. Without this primitive, CW-1 cannot be resolved.

### AD-2 — `BranchInjectionInterceptor` missing [pos-api]

Referenced in defensive comments (e.g. [AccessControlService.cs:139-141](../POS.Services/Service/AccessControlService.cs#L139-L141)) but absent from `POS.Repository/Interceptors/`. Without it, CW-2 depends 100% on developer discipline.

### AD-3 — Hardcoded UX copy with vertical awareness [restaurant-app]

- Switch on `PosExperience` for product placeholder text (`product-form.component.ts:175-217`).
- `if (currentSubCategory === Gym || currentMacro === Services)` for sidebar label (`admin-shell.component.ts:158-161`).
- Branded text mixed with logic; should externalize to `i18n.copy.{macro}.json` loaded dynamically.

### AD-4 — Partial virtual hardware implementations [pos-local-bridge]

Only `VirtualBiometricScanner` exists. Turnstile, Printer, SerialInput require manual DI rewiring for headless CI.

### AD-5 — `SerialInput` without `ReadTimeout` [pos-local-bridge]

`ContinuousSerialScanner.cs:19-20` — silent hang on COM port not detected until process shutdown. Operationally invisible.

### AD-6 — `FinoCloudClient` hardcodes hub URL [pos-local-bridge]

No interface to inject `HubConnection` for testing. Any integration test of cloud-to-bridge flow requires upstream refactor.

### AD-7 — `HealthMonitor` reports hardcoded `biometrics = 1` [pos-local-bridge]

`HealthMonitor.cs:86` ignores whether biometrics should actually be active for the tenant. Misleading telemetry for non-biometric tenants.

---

## 7. Testing Strategy Gaps

### TG-1 — pos-api has zero test projects

**Missing primitive:**

```text
JwtTestFactory.CreateDeviceToken(businessId, branchId, mode, features[])
JwtTestFactory.CreateUserToken(businessId, branchId, userId, role)
```

**Where hooks must be implemented:**

- New `POS.IntegrationTests/` project with `WebApplicationFactory<Program>`.
- `MultiTenantSeedFixture` that bootstraps N businesses + branches + JWTs per test.
- First critical test: `TenantIsolationTests.GetOrders_FromBusinessA_DoesNotReturnDataFromBusinessB`.
- Feature-gate tests: `FeatureGateTests.RestaurantTenant_Cannot_POST_AccessControl_Endpoint`.

### TG-2 — restaurant-app has 1 unit spec out of 246 TS files

**Missing primitives:**

- `restaurant-app:e2e/fixtures/jwt-mock.ts` — Playwright fixture intercepting `/api/Auth/login` and returning a synthetic JWT with chosen claims.
- Unit tests for `TenantContextService.isApplicableToGiro()` over all 4 macros × all feature keys.
- Tests for `*appFeature` structural directive with varying tenant contexts.

**Where hooks must be implemented:**

- Suites: `restaurant-app:e2e/tests/verticals/{restaurant,gym,retail,pharmacy}.spec.ts`, each with `seedDeviceConfig()` + JWT mock.
- Snapshot/visual regression: `UnifiedPosComponent` per `PosExperience` (grid vs keypad).
- TestBed setup for mocking `TenantContextService` with preset macro/feature combos.

### TG-3 — pos-local-bridge missing E2E harness

**Missing primitives:**

- `VirtualTurnstileController`, `VirtualPrinterRouter`, `VirtualSerialInputScanner` (following the `VirtualBiometricScanner` pattern).
- `MockSignalRHub` test fixture acting as a fake cloud — programmatically emits `OpenTurnstile` / `SendEscPosCommand` / `SyncAccessData`.
- Refactor of `FinoCloudClient` to accept injected `HubConnection` (resolves AD-6).

**Where hooks must be implemented:**

- New `PosLocalBridge.IntegrationTests/` with mock hub orchestration.
- Claim-validation tests that depend on AD-1 being resolved first (the supervisor must read claims before it can be tested for honoring them).

### TG-4 — Cross-system E2E inexistent

No infrastructure exists for the full flow:

```
Gym tenant → POST /api/Hardware/print on pos-api
           → HardwareController dispatches SendEscPosCommand via SignalR
           → pos-local-bridge receives event on bridge-hardware-{branchId} group
           → PrinterSupervisor → IPrinterRouter → physical (or virtual) print
```

Better strategy than monolithic E2E: **contract tests (Pact-style) between pairs of repos**. Backend-bridge contract validates SignalR message shape + casing (relevant given the `[JsonPropertyName]` PascalCase workaround on `EscPosPayloadDto`). Backend-frontend contract validates response DTOs + camelCase wire format.

---

## 8. Summary Matrix

| Vector | pos-api | restaurant-app | pos-local-bridge |
|---|---|---|---|
| **A — Domain Flexibility** | PASS — `FeatureGateService` 3-D matrix, DB-driven | PASS — `GIRO_FEATURE_MAP` declarative, vertical-agnostic routes (UX copy debt only) | N/A |
| **B — Architectural Extensibility** | WEAK — no Global Query Filters; manual tenant scoping per query | GOOD — polymorphic routing, signal-driven layouts (forms are static templates) | CRITICAL — all 7 supervisors always-on; JWT never decoded |
| **C — Testability** | FAIL — zero test projects | SPARSE — 1/246 unit specs; 5 E2E without JWT mock | PARTIAL — unit OK; no virtual Turnstile/Printer/Serial; no SignalR loopback |

---

## 9. Suggested Priority Order (Highest ROI First)

1. **`IDeviceClaimsReader` + claim-gated supervisors in pos-local-bridge** — resolves CW-1 + AD-1. Unblocks per-vertical resource efficiency.
2. **`BranchInjectionInterceptor` + Global Query Filters in pos-api** — resolves CW-2 + AD-2. Closes the highest-risk regulatory exposure.
3. **`JwtTestFactory` + `POS.IntegrationTests` in pos-api** — resolves TG-1. Enables the safety net for items 1-2 before shipping.
4. **Virtual hardware implementations + `MockSignalRHub` in pos-local-bridge** — resolves AD-4 + AD-6 + TG-3. Enables headless CI of the bridge.
5. **JWT mock fixture + multi-vertical specs in restaurant-app** — resolves TG-2. Catches UI regressions per vertical before customers do.
6. **Schema-driven forms in restaurant-app** — resolves CW-3. Larger scope; probably Phase 2.

---

## Appendix — File Reference Index

### pos-api
- [POS.Domain/Enums/FeatureKey.cs](../POS.Domain/Enums/FeatureKey.cs)
- [POS.Services/Service/FeatureGateService.cs](../POS.Services/Service/FeatureGateService.cs)
- [POS.API/Filters/RequiresFeatureAttribute.cs](../POS.API/Filters/RequiresFeatureAttribute.cs)
- [POS.Domain/Models/Catalogs/MacroCategory.cs](../POS.Domain/Models/Catalogs/MacroCategory.cs)
- [POS.Services/Service/BranchService.cs](../POS.Services/Service/BranchService.cs)
- [POS.Repository/ApplicationDbContext.cs](../POS.Repository/ApplicationDbContext.cs)
- [POS.Repository/GenericRepository.cs](../POS.Repository/GenericRepository.cs)
- [POS.Services/Service/AccessControlService.cs](../POS.Services/Service/AccessControlService.cs)

### restaurant-app (sibling repo: `d:/Source/restaurant-app`)
- `src/app/core/enums/config.enum.ts` — `MacroCategoryType`, `SubCategoryType`
- `src/app/core/enums/feature-key.enum.ts` — `FeatureKey`, `GIRO_FEATURE_MAP`
- `src/app/core/utils/jwt.utils.ts` — `extractMacroCategoryFromJwt()`
- `src/app/core/services/tenant-context.service.ts` — `isApplicableToGiro()`
- `src/app/core/services/device-routing.service.ts` — `MACRO_POS_EXPERIENCE`
- `src/app/modules/admin/admin-shell.component.ts` — sidebar
- `src/app/modules/pos/components/unified-pos/unified-pos.component.ts` — chameleon POS
- `src/app/modules/admin/components/products/product-form/product-form.component.ts` — form static template
- `src/app/shared/directives/app-feature.directive.ts` — structural feature gate
- `e2e/fixtures/device-config.ts` — existing fixture (no JWT equivalent)
- `playwright.config.ts`, `e2e/tests/*.spec.ts` — E2E baseline

### pos-local-bridge (sibling repo: `d:/Source/pos-local-bridge`)
- `PosLocalBridge.Host/Program.cs` — supervisor registration (lines 56-63)
- `PosLocalBridge.Host/Worker.cs` — pairing + SignalR start (lines 29-58)
- `PosLocalBridge.Host/{Turnstile,Printer,Biometric,SerialInput,AccessSync}Supervisor.cs` — always-on workers
- `PosLocalBridge.Host/HealthMonitor.cs` — hardcoded `biometrics = 1` (line 86)
- `PosLocalBridge.Transport/Cloud/FinoCloudClient.cs` — no JWT decode
- `PosLocalBridge.Security/HttpPairingService.cs` — pairing log (line 80)
- `PosLocalBridge.Contracts/Security/PairingResponse.cs` — claim DTO
- `PosLocalBridge.Hardware.Biometrics/VirtualBiometricScanner.cs` — only virtual hardware
- `PosLocalBridge.Tests/Host/BiometricSupervisorTests.cs` — example NSubstitute pattern
