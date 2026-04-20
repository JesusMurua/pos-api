# BDD-012 — Session Auth, Session-Type Claims & Role Hardening
**Fase:** 17 | **Estado:** Proposed | **Fecha:** 2026-04-19
**Documentos relacionados:** [AUDIT-011-backend-roles-devices.md](AUDIT-011-backend-roles-devices.md)

---

## 1. Executive Summary

### 1.1 Problem Statement

The authentication layer in `AuthService` produces **indistinguishable User Tokens** for email-password and PIN logins (both carry identical claim shapes, differing only in expiration). The frontend cannot tell whether a session is a back-office admin session or an in-branch PIN session, causing incorrect routing between POS and Back Office shells. Additionally, `Business.OnboardingCompleted` is baked into the JWT at login time — when a user completes onboarding and reloads the SPA, the stale token redirects them back to `/setup` in an infinite loop. Finally, the `Host` role is denied access to `GET /api/Table*` endpoints even though the floor-map is central to host duties, returning 403 Forbidden.

### 1.2 Proposed Solution

Introduce a `sessionType` claim (`email` | `pin`) in the standard user JWT while keeping the existing `type=device` claim on device tokens untouched. Add a new `GET /api/auth/me` endpoint that rehydrates the session from the database and re-issues a fresh JWT + `AuthResponse`, ensuring `OnboardingCompleted`, `CurrentOnboardingStep`, and `OnboardingStatusId` reflect current DB state. Extend `AuthResponse` with the two missing onboarding fields. Grant `Host` read-only access to the two table-list endpoints. Add a defensive normalizer so `RoleId = 2` behaves as `Manager` regardless of which label a legacy DB row carries.

### 1.3 Expected Outcome / Impact

- The frontend can deterministically branch between Back Office vs POS shells without inspecting roles heuristically.
- The `/setup` redirect loop is eliminated: the SPA can call `/api/auth/me` on boot and trust its response as the source of truth.
- Hosts can view and monitor tables and floor-map status without loss of control-surface restrictions (no write privileges added).
- Any stray `Admin` label in the DB or JWT no longer misroutes the frontend.

---

## 2. Current State Analysis

### 2.1 Authentication Flows Today

| Flow | Service Method | Token Shape | TTL |
|---|---|---|---|
| Email + password | `EmailLoginAsync(email, password)` | Full user token (10 claims) | `OwnerExpirationDays` days |
| Staff PIN | `PinLoginAsync(branchId, pin)` | **Same** full user token (10 claims) | `PinExpirationHours` hours |
| Branch switch | `SwitchBranchAsync(userId, branchId)` | Full user token (10 claims) | Owner/Manager days, else hours |
| Device | `GenerateDeviceToken(device, business, features)` | Device token with `type=device`, **no** userId/roleId | 10 years |

**Claims emitted by `GenerateToken` (user tokens) today:**
```
NameIdentifier (userId), Role, Name, businessId, branchId,
branches (JSON), planType, macroCategory, trialEndsAt,
onboardingCompleted, features (JSON)
```

**Missing from user tokens:** `sessionType` discriminator. The frontend cannot tell email vs PIN apart.

### 2.2 AuthResponse Shape Today

Defined in `POS.Services/IService/IAuthService.cs`:

```
Token, RoleId, Name, BusinessId, CurrentBranchId, Branches,
PlanTypeId, PrimaryMacroCategoryId, TrialEndsAt,
SubscriptionStatus, OnboardingCompleted
```

**Missing for onboarding rehydration:** `CurrentOnboardingStep`, `OnboardingStatusId`. Both columns exist on `Business` ([POS.Domain/Models/Business.cs:32-35](../POS.Domain/Models/Business.cs#L32-L35)) but are never surfaced.

### 2.3 TableController Authorization Today

| Endpoint | Roles |
|---|---|
| `GET /api/Table` | Owner, Manager, Cashier, Waiter, Kitchen |
| `GET /api/Table/status` | Owner, Manager, Cashier, Waiter, Kitchen |
| `POST /api/Table` | Owner |
| `PUT /api/Table/{id}` | Owner |
| `PATCH /api/Table/{id}/toggle` | Owner |
| `PATCH /api/Table/{id}/status` | Owner, Manager, Cashier, Waiter |

`Host` is absent from every row → 403 Forbidden on every table endpoint.

### 2.4 Stale Token — Current Symptom

1. User registers → `Business.OnboardingCompleted = false` baked into JWT claim `onboardingCompleted=false`.
2. User completes onboarding → DB column flips to `true`, JWT untouched.
3. User reloads SPA → Angular reads stale JWT → redirects to `/setup`.
4. There is **no endpoint** that returns fresh session state; the user must log out and log in again to get a new token.

### 2.5 `UserRole.cs` / `UserRoleIds.cs` Today

```
UserRole enum : Owner, Manager, Cashier, Kitchen, Waiter, Kiosk, Host
UserRoleIds   : Owner=1, Manager=2, Cashier=3, Kitchen=4, Waiter=5, Kiosk=6, Host=7
```

No `Admin` member exists. However, third-party tooling or legacy migrations may emit `Role = "Admin"` with `RoleId = 2`. The current `ToCode(int id)` helper lacks a defensive mapping for any label variation.

---

## 3. Technical Requirements

### 3.1 Functional Requirements

| ID | Requirement | Acceptance Criteria |
|---|---|---|
| **FR-001** | Inject `sessionType` claim in every user JWT | `EmailLoginAsync` emits `sessionType=email`; `PinLoginAsync` emits `sessionType=pin`; `SwitchBranchAsync` preserves the original `sessionType` from the incoming token; `RegisterAsync` emits `sessionType=email`. Device tokens remain untouched with their existing `type=device` claim. |
| **FR-002** | Grant `Host` read access to table list endpoints | `GET /api/Table` and `GET /api/Table/status` accept `Host` in their `[Authorize(Roles=…)]` list. All other Table endpoints remain unchanged (Host still 403 on write routes). |
| **FR-003** | New endpoint `GET /api/auth/me` | Authenticated endpoint that reads `ClaimTypes.NameIdentifier` from the current user, fetches fresh `User` + `Business` + `Subscription` + `Branches` + features, re-issues a new JWT with the same `sessionType` as the incoming token, and returns a fully populated `AuthResponse`. Returns `401` if the user/business is no longer active. |
| **FR-004** | Extend `AuthResponse` with onboarding fields | `AuthResponse` exposes `CurrentOnboardingStep` (int) and `OnboardingStatusId` (int). All four existing login/register/switch flows populate them. |
| **FR-005** | Defensive handling of `RoleId = 2` | `UserRoleIds.ToCode(2)` returns `"Manager"` regardless of any alias. `IsAdminRole(roleId)` helper treats `Owner (1)` and `Manager (2)` as admin. Any legacy DB value of `"Admin"` is normalized to `Manager` at the service boundary. No code path emits `"Admin"` into a new JWT. |

### 3.2 Non-Functional Requirements

- **Performance:** `GET /api/auth/me` must complete in <150 ms at P95. It performs ≤4 reads (User, Business, Subscription, Branches collection) and one feature-gate evaluation. No N+1 queries.
- **Security:**
  - Endpoint is `[Authorize]` (requires any valid JWT, user or device).
  - Device tokens (`type=device`) must NOT be accepted on `/api/auth/me` — explicit rejection with `401`, since the endpoint rehydrates a *user* session.
  - The re-issued JWT must have a fresh expiration based on the session type: `OwnerExpirationDays` for admin roles and email sessions, `PinExpirationHours` for PIN sessions.
  - Changes to `sessionType` must be non-forgeable: the claim is set by the service based on entry method, never read from input.
- **Backward compatibility:**
  - Existing clients that do not read `sessionType` must keep working. The claim is additive.
  - Existing clients that already read `OnboardingCompleted` from `AuthResponse` must keep working; the two new fields are additive.

---

## 4. Architecture Design

### 4.1 Component Overview

| Component | Change |
|---|---|
| `IAuthService` | Add `GetSessionAsync(int userId, string sessionType)` method |
| `AuthService` | Implement `GetSessionAsync`; add `sessionType` parameter to `GenerateToken` private method; extend all four public auth methods to pass `sessionType`; extend all four to populate new `AuthResponse` fields |
| `AuthResponse` (DTO) | Add `CurrentOnboardingStep: int`, `OnboardingStatusId: int` |
| `AuthController` | New action `[HttpGet("me")] Me()` → delegates to `IAuthService.GetSessionAsync` |
| `TableController` | `Host` appended to `[Authorize(Roles = …)]` on `GetByBranch` and `GetTableStatuses` |
| `UserRoleIds` (helper) | Add `public static bool IsAdminRole(int roleId)` and defensive mapping |
| No new repositories | All reads go through existing `IUnitOfWork` members (`Users`, `Business`, `Subscriptions`, `UserBranches`, `Branches`, `Catalog`) |

### 4.2 Data Flow — `GET /api/auth/me`

1. Request arrives with a bearer token. Authentication middleware validates the JWT signature + expiration.
2. `AuthController.Me()` reads `ClaimTypes.NameIdentifier` from `User`. If missing or non-integer → `401 Unauthorized`.
3. If the token carries `type=device` claim → `401 Unauthorized` (device tokens have no user identity to rehydrate).
4. Controller reads the incoming `sessionType` claim (fallback: `"email"` for legacy tokens that predate this BDD).
5. Controller calls `IAuthService.GetSessionAsync(userId, sessionType)`.
6. Service:
   - Loads `User` by id → if null or `!IsActive` → throw `UnauthorizedException`.
   - Loads `Business` by `user.BusinessId` → if null or `!IsActive` → throw `UnauthorizedException`.
   - Resolves `(currentBranchId, branches)` via existing `ResolveBranchesAsync(user)`.
   - Resolves subscription via `ResolveSubscriptionAsync`.
   - Resolves plan code + macro code + features (same path as `EmailLoginAsync`).
   - Computes expiration: `IsAdminRole(user.RoleId) ? OwnerExpirationDays : PinExpirationHours`, overridden by `sessionType == "email" ? OwnerExpirationDays : PinExpirationHours`.
   - Calls `GenerateToken(user, business, currentBranchId, branches, expiration, planType, macroCode, features, sessionType)`.
   - Returns a populated `AuthResponse` including new onboarding fields.
7. Controller returns `200 OK` with `AuthResponse` in the body.

### 4.3 Data Flow — `sessionType` Injection

| Source | `sessionType` value | Notes |
|---|---|---|
| `EmailLoginAsync` | `"email"` | New literal passed to `GenerateToken` |
| `PinLoginAsync` | `"pin"` | New literal passed to `GenerateToken` |
| `SwitchBranchAsync` | Carried over from incoming token's `sessionType` claim; default `"email"` if absent | Preserves the original login modality across branch switches |
| `RegisterAsync` | `"email"` | New user's first session is email-based |
| `GetSessionAsync` | Same as incoming token; default `"email"` if absent | Endpoint preserves the original session's type |
| `GenerateDeviceToken` | **not added** | Device tokens use the existing `type=device` claim — do not duplicate |

### 4.4 Database Schema Changes

**None.** All required columns already exist on `Business`:
- `OnboardingCompleted` — [Business.cs:29](../POS.Domain/Models/Business.cs#L29)
- `OnboardingStatusId` — [Business.cs:32](../POS.Domain/Models/Business.cs#L32)
- `CurrentOnboardingStep` — [Business.cs:35](../POS.Domain/Models/Business.cs#L35)

No migration is required.

---

## 5. API Contract

### 5.1 New Endpoint — `GET /api/auth/me`

**Purpose:** Rehydrate the current session from the database and return a freshly minted JWT along with an up-to-date `AuthResponse`. Used by the SPA on boot, after completing onboarding, and whenever a `402`/stale-state response is detected.

**Authorization:** `[Authorize]` — any valid user JWT. Device tokens (with `type=device`) are rejected with `401`.

**Request payload:** None (query string empty, body empty).

**Response payload:** `AuthResponse` (see §5.3).

**HTTP status codes:**

| Status | Meaning |
|---|---|
| `200 OK` | Session successfully rehydrated; response body contains fresh JWT + state |
| `401 Unauthorized` | JWT missing, invalid, expired, user deactivated, business deactivated, or token is a device token |
| `404 Not Found` | `userId` from claim no longer resolves to a user row (extremely rare; indicates deleted user) |

**Example response body shape:**

```
{
  "token": "eyJhbGciOiJIUzI1Ni…",
  "roleId": 1,
  "name": "Jesús Murúa",
  "businessId": 42,
  "currentBranchId": 51,
  "branches": [ { "id": 51, "name": "Principal" } ],
  "planTypeId": 2,
  "primaryMacroCategoryId": 3,
  "trialEndsAt": "2026-05-01T00:00:00.0000000Z",
  "subscriptionStatus": "trialing",
  "onboardingCompleted": true,
  "currentOnboardingStep": 5,
  "onboardingStatusId": 3
}
```

### 5.2 Modified Endpoints

| Endpoint | Change |
|---|---|
| `POST /api/auth/email-login` | Response now includes `currentOnboardingStep` and `onboardingStatusId`. JWT now carries `sessionType=email`. |
| `POST /api/auth/pin-login` | Response now includes `currentOnboardingStep` and `onboardingStatusId`. JWT now carries `sessionType=pin`. |
| `POST /api/auth/switch-branch` | Response includes new onboarding fields. JWT carries `sessionType` inherited from input. |
| `POST /api/auth/register` | Response includes new onboarding fields (both default to initial values). JWT carries `sessionType=email`. |
| `GET /api/Table` | `[Authorize(Roles = "Owner,Manager,Cashier,Waiter,Kitchen,Host")]` |
| `GET /api/Table/status` | `[Authorize(Roles = "Owner,Manager,Cashier,Waiter,Kitchen,Host")]` |

All other endpoints remain unchanged.

### 5.3 Extended `AuthResponse` DTO

Field-level contract (in `POS.Services/IService/IAuthService.cs`):

| Field | Type | New? | Notes |
|---|---|---|---|
| `Token` | `string` | | Freshly minted JWT |
| `RoleId` | `int` | | From `User.RoleId` |
| `Name` | `string` | | From `User.Name` |
| `BusinessId` | `int` | | |
| `CurrentBranchId` | `int` | | |
| `Branches` | `List<BranchSummary>` | | |
| `PlanTypeId` | `int` | | |
| `PrimaryMacroCategoryId` | `int` | | |
| `TrialEndsAt` | `string?` | | ISO-8601 |
| `SubscriptionStatus` | `string?` | | |
| `OnboardingCompleted` | `bool` | | |
| `CurrentOnboardingStep` | `int` | **NEW** | From `Business.CurrentOnboardingStep` |
| `OnboardingStatusId` | `int` | **NEW** | From `Business.OnboardingStatusId` |

### 5.4 Service Interface Changes — `IAuthService`

**New method signature (conceptual):**

```
Task<AuthResponse> GetSessionAsync(int userId, string sessionType)
```

- **Parameters:**
  - `userId`: from JWT `NameIdentifier` claim.
  - `sessionType`: `"email"` or `"pin"` — taken from incoming JWT's `sessionType` claim; defaults to `"email"` if absent.
- **Returns:** `AuthResponse` with fresh token and DB-sourced state.
- **Exceptions:**
  - `UnauthorizedException` → user or business not found or inactive.
  - `ValidationException("User has no assigned branch")` → propagated from `ResolveBranchesAsync` when the user has no branches (edge case: abandoned PIN user).

**Modified private method signature (conceptual):**

```
string GenerateToken(
    User user, Business business, int branchId, List<BranchSummary> branches,
    TimeSpan expiration, string planType, string macroCategoryCode,
    IReadOnlyList<string> features, string sessionType)
```

The last parameter is new. All four public methods in `AuthService` must be updated to pass it. `GenerateDeviceToken` is **not** modified.

### 5.5 Controller Interface — `AuthController.Me`

| Aspect | Value |
|---|---|
| Route | `GET /api/auth/me` |
| Attribute | `[Authorize]` |
| ProducesResponseType | `AuthResponse` (200), none (401), none (404) |
| Body | None |
| Reads from `User` claims | `ClaimTypes.NameIdentifier`, `sessionType`, `type` (to reject device tokens) |

---

## 6. Business Logic Specifications

### 6.1 `sessionType` Resolution Algorithm

```
read sessionType claim from incoming JWT
if claim is absent OR empty:
    sessionType := "email"     // legacy token fallback
else:
    sessionType := claim value  // trust JWT signature, never input payload
```

### 6.2 `IsAdminRole` Algorithm

```
function IsAdminRole(roleId: int): bool
    return roleId == UserRoleIds.Owner (1)
        OR roleId == UserRoleIds.Manager (2)
```

This helper replaces the inline check at [AuthService.cs:141](../POS.Services/Service/AuthService.cs#L141). All callers use the helper.

### 6.3 Role Label Normalization — `UserRoleIds.ToCode`

```
function ToCode(id: int): string
    switch id:
        1 => "Owner"
        2 => "Manager"     // <- also covers any legacy "Admin" value, since we emit by id
        3 => "Cashier"
        4 => "Kitchen"
        5 => "Waiter"
        6 => "Kiosk"
        7 => "Host"
        default => "Cashier"
```

This is already the current behavior — the BDD merely codifies it and forbids any new code path that emits `"Admin"` as a role string. Any DB seed or fixture that sets `Role = "Admin"` must be migrated out of tree; legacy rows with `RoleId = 2` remain valid because the code is derived from the id, not the stored string.

### 6.4 Validation Rules

| ID | Rule | Error |
|---|---|---|
| **VR-001** | `GET /api/auth/me` with device token must be rejected | `401 Unauthorized` — "Device tokens cannot rehydrate user sessions" |
| **VR-002** | `GET /api/auth/me` with deactivated user must be rejected | `401 Unauthorized` — "User is no longer active" |
| **VR-003** | `GET /api/auth/me` with deactivated business must be rejected | `401 Unauthorized` — "Business is no longer active" |
| **VR-004** | `sessionType` values other than `"email"` or `"pin"` in an incoming JWT must coerce to `"email"` | Silent normalization — do not 401 |

### 6.5 Edge Cases

| Case | Behavior |
|---|---|
| Legacy token issued before this BDD (no `sessionType` claim) | Treated as `"email"`. Re-issued token includes the claim, self-healing on next call. |
| User whose `Role` was manually set to `"Admin"` in DB | `RoleId = 2` drives everything; `ToCode(2)` emits `"Manager"`. Frontend sees `Role = Manager`, routes to Back Office correctly. |
| User active but business inactive | `401` — business-level deactivation cascades to sessions. |
| User active, business active, but user has no branches and is not Owner | Propagates existing `ValidationException("User has no assigned branch")` as `400` per existing handler. |
| `GET /api/auth/me` called while a branch-switch is mid-flight | Endpoint is idempotent; it reflects the last-committed `UserBranch` state. |
| Host calls `PATCH /api/Table/{id}/toggle` | Still `403 Forbidden` — Host is only added to the two GET endpoints. |

---

## 7. Performance Optimization Strategy

### 7.1 Query Plan for `GET /api/auth/me`

| Query | Source | Notes |
|---|---|---|
| `Users.GetByIdAsync` | Existing repository | Single row by PK |
| `Business.GetByIdAsync` | Existing repository | Single row by PK |
| `Subscriptions.GetByBusinessIdAsync` | Existing repository | Single row by FK |
| `UserBranches.GetByUserIdAsync` + `Branches` nav | Existing repository | Include `Branch` to avoid N+1 |
| `Catalog.GetMacroCategoriesAsync` | Existing repository | Cached in-memory (already) |
| `IFeatureGateService.GetEnabledFeaturesAsync` | Existing service | Cached per business (already) |

**Total round-trips:** 4–5 DB reads + 0 feature-gate reads on warm cache. Target P95 <150 ms.

### 7.2 Caching Strategy

No new cache is introduced. Existing caches (`MacroCategories`, feature matrix per business) remain the hot path. The endpoint **intentionally bypasses any per-user cache** because its purpose is to refresh stale state — serving a cached response would defeat the point.

### 7.3 Bulk Operations / Transactions

Not applicable — `/api/auth/me` is a read-only endpoint. No writes to DB. No transaction required.

---

## 8. Error Handling Strategy

| Scenario | Exception | HTTP Status | Body |
|---|---|---|---|
| Missing JWT | (ASP.NET Core auth middleware) | `401` | Default challenge response |
| Device token on `/me` | `UnauthorizedException("Device tokens cannot rehydrate user sessions")` | `401` | JSON error per middleware |
| User not found / inactive | `UnauthorizedException("User is no longer active")` | `401` | JSON error |
| Business inactive | `UnauthorizedException("Business is no longer active")` | `401` | JSON error |
| User has no branches | `ValidationException("User has no assigned branch")` | `400` | JSON error (existing handler) |

**Logging requirements:**
- INFO on successful `/me` hit with `userId`, `businessId`, `sessionType`.
- WARN on `401` due to deactivated user/business (helps detect offboarding-in-progress).
- No PII beyond userId in logs.

---

## 9. Testing Requirements

### 9.1 Unit Test Scenarios — `AuthService.GetSessionAsync`

- Returns fresh `OnboardingCompleted=true` when DB flipped after token was issued.
- Populates `CurrentOnboardingStep` and `OnboardingStatusId` from DB.
- Preserves `sessionType="pin"` when input carries `sessionType="pin"`.
- Preserves `sessionType="email"` when input omits the claim (legacy fallback).
- Throws `UnauthorizedException` when user is inactive.
- Throws `UnauthorizedException` when business is inactive.
- Issues admin-length expiration when role is Owner or Manager.
- Issues PIN-length expiration when role is Cashier/Waiter/Kitchen/Host and `sessionType=pin`.
- Branch list matches `ResolveBranchesAsync` output.

### 9.2 Unit Test Scenarios — `GenerateToken` (with new param)

- Emits `sessionType=email` when called with `"email"`.
- Emits `sessionType=pin` when called with `"pin"`.
- Existing 10 claims still present (no regression).

### 9.3 Integration Test Scenarios

- `POST /api/auth/email-login` → resulting JWT contains `sessionType=email`.
- `POST /api/auth/pin-login` → resulting JWT contains `sessionType=pin`.
- `POST /api/auth/switch-branch` with `pin` input → resulting JWT preserves `sessionType=pin`.
- `GET /api/auth/me` with valid user token → `200` + fresh state.
- `GET /api/auth/me` with device token → `401`.
- `GET /api/auth/me` with expired user token → `401`.
- `GET /api/Table` as `Host` → `200` with tables.
- `GET /api/Table/status` as `Host` → `200` with statuses.
- `POST /api/Table` as `Host` → `403`.
- `PATCH /api/Table/{id}/status` as `Host` → `403`.

### 9.4 Performance Test Criteria

- `GET /api/auth/me` P95 < 150 ms under 50 RPS sustained load.
- No regression in login endpoints (they now do the same work plus one added claim).

---

## 10. Implementation Phases

| Phase | Scope | Dependencies | Complexity |
|---|---|---|---|
| **Phase 1** | Extend `AuthResponse` DTO with `CurrentOnboardingStep` + `OnboardingStatusId`; populate them in all four existing auth flows. | None | **Low** |
| **Phase 2** | Add `sessionType` parameter to `GenerateToken`; update all four call sites; include claim in JWT. | Phase 1 | **Low** |
| **Phase 3** | Add `IAuthService.GetSessionAsync` + `AuthController.Me`; reject device tokens; preserve `sessionType`. | Phase 2 | **Medium** |
| **Phase 4** | Update `TableController` attributes: add `Host` to the two GET endpoints. | None (can ship in parallel) | **Low** |
| **Phase 5** | Add `UserRoleIds.IsAdminRole` helper; replace inline admin check at [AuthService.cs:141](../POS.Services/Service/AuthService.cs#L141); verify `ToCode(2)` consistently returns `"Manager"`. | None | **Low** |

Phases 1–3 are sequentially dependent. Phases 4 and 5 are independent and may ship alongside Phase 1 or later.

---

## 11. Out of Scope

- No new `Device`-side changes (already covered by [AUDIT-011](AUDIT-011-backend-roles-devices.md) / future BDDs).
- No refresh-token mechanism (sliding sessions are out of scope; `/me` is a manual rehydrate, not an automatic refresh).
- No changes to PIN rotation, BCrypt policy, or password complexity rules.
- No changes to `features` claim or feature-gate logic.
- No data migration for existing tokens in the wild — the `sessionType` claim is additive and backward-compatible; legacy tokens survive until natural expiration.

---

## 12. Security Considerations

- The `sessionType` claim must be **signed into the JWT**, never read from request headers/body/query. Signature validation is the integrity boundary.
- Rejecting device tokens on `/api/auth/me` prevents KDS/kiosk hardware from obtaining a user token via rehydration.
- The re-issued JWT's expiration is bounded by `sessionType`: a PIN session cannot upgrade itself to an admin-length token even if the underlying user is Owner.
- No secrets are logged. No PII beyond `userId` appears in server logs.
- The endpoint is rate-limited by the existing ASP.NET global policy; no per-route throttling is introduced (the read is lightweight and idempotent).
