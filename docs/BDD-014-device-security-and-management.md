# BDD-014 — Device Security & Back Office Management
**Fase:** 19 | **Estado:** Proposed | **Fecha:** 2026-04-20
**Documentos relacionados:**
- [AUDIT-011-backend-roles-devices.md](AUDIT-011-backend-roles-devices.md) — Architectural gap analysis for Device identity.
- [AUDIT-032-admin-devices-list.md](AUDIT-032-admin-devices-list.md) — Frontend audit confirming the Back Office list is 100 % greenfield.
- [BDD-012-session-auth-and-roles.md](BDD-012-session-auth-and-roles.md) — Introduces `sessionType` and confirms device tokens use the `type=device` discriminator.

---

## 1. Executive Summary

### 1.1 Problem Statement

Device tokens are currently emitted with a **10-year expiration** by [`AuthService.GenerateDeviceToken`](../POS.Services/Service/AuthService.cs) (`expires: DateTime.UtcNow.AddYears(10)`). The authorization pipeline only validates the JWT signature — it never consults `Device.IsActive`. Consequently, toggling `IsActive = false` on a device **does not revoke the in-flight token**: the device keeps calling protected endpoints until the year 2036. There is also no `GET /api/devices` endpoint, no admin actions (toggle, rename, delete), and no DTO that projects `BranchName` for a list view — `IDeviceRepository.GetByBranchAsync` exists but is orphaned, consumed by no service or controller.

### 1.2 Proposed Solution

Introduce a lightweight `IAsyncAuthorizationFilter` (`DeviceActiveAuthorizationFilter`) that, **only** when the incoming JWT carries `type=device`, validates `Device.IsActive` per request with a short-TTL `IMemoryCache` layer keyed by `deviceId`. A sibling `DeviceActiveHubFilter` implementing `IHubFilter` applies the same gate to SignalR connections and invocations, sharing the cache with the MVC filter so a single revocation covers both pipelines. Admin endpoints `PATCH /api/devices/{id}/toggle-active` and `PATCH /api/devices/{id}` (rename and/or reassign branch) explicitly invalidate the affected cache entry so revocation propagates within a single request cycle rather than waiting for TTL expiry. Ship the management API in parallel: `GET /api/devices` with optional `?branchId=` filter, a purpose-built `DeviceListItemResponse` DTO that projects `BranchName` via a single `Include`, and all admin actions scoped to `Owner`/`Manager` within the caller's business with opaque 404 on cross-tenant ids.

### 1.3 Expected Outcome / Impact

- **Security:** A revoked device cannot call any protected endpoint after `toggle-active` returns, eliminating the 10-year token loophole.
- **Operational visibility:** Back Office gains a live list of terminals with `Name`, branch, mode, status and last-seen timestamp — the view [AUDIT-032](AUDIT-032-admin-devices-list.md) documented as greenfield becomes functional.
- **Performance cost:** ≤1 extra DB roundtrip per device request on cache miss (every 60 s per device); cache hit rate should exceed 99 % under steady-state traffic.
- **Deferred scope eliminated:** The existing `IDeviceRepository.GetByBranchAsync` method gets wired, stopping the drift between repository surface and API surface.

---

## 2. Current State Analysis

### 2.1 Device Token Emission (Security Baseline)

[`AuthService.GenerateDeviceToken`](../POS.Services/Service/AuthService.cs) emits a JWT with:

```
claims:
  type=device, deviceId, businessId, branchId, mode, planType, macroCategory, features
expires: UtcNow + 10 years
signing: HmacSha256 with JwtSettings.Secret
```

**No `IsActive` check** anywhere in the pipeline. JWT Bearer middleware validates signature + expiration and terminates.

### 2.2 `Device` Entity

[`POS.Domain/Models/Device.cs`](../POS.Domain/Models/Device.cs) already persists everything needed:

| Field | Notes |
|---|---|
| `Id` | PK, int |
| `BusinessId` | FK to Business — enables tenant scoping without join |
| `BranchId` | FK to Branch |
| `DeviceUuid` | Client-generated UUID |
| `Name` | Human label |
| `Mode` | `cashier` / `kiosk` / `tables` / `kitchen` |
| `IsActive` | **Present but never read in auth pipeline** |
| `LastSeenAt` | Updated by `PUT /api/devices/heartbeat/{uuid}` every 5 min |
| `ActivatedAt`, `CreatedAt` | Timestamps |

### 2.3 Endpoint Inventory

| Method | Route | Auth | Purpose |
|---|---|---|---|
| POST | `/api/Device/generate-code` | Owner/Manager | Create activation code |
| POST | `/api/Device/activate` | Anon | Redeem code |
| POST | `/api/Device/setup` | Anon | Owner email alt-flow |
| POST | `/api/Devices/register` | Owner/Manager | Upsert device + mint device JWT |
| PUT | `/api/Devices/heartbeat/{uuid}` | Authorize | Ping `LastSeenAt` |
| GET | `/api/Devices/validate/{uuid}` | Anon | Self-config by UUID |

**Gaps confirmed:** no list endpoint, no toggle/update/delete, no revocation. `IDeviceRepository.GetByBranchAsync(int)` defined in [`IDeviceRepository.cs:7`](../POS.Repository/IRepository/IDeviceRepository.cs#L7) is unused.

### 2.4 Existing `DeviceResponse` DTO

[`DeviceResponse.cs`](../POS.Domain/DTOs/Device/DeviceResponse.cs) carries `Id, DeviceUuid, Mode, Name, IsActive, BranchId, LastSeenAt, CreatedAt, DeviceToken?`. **Missing for list UX:** `BranchName`. Single-item projection would force the UI to cross-reference a separate `/api/branches` call.

### 2.5 Decision — Option A vs Option B (from task context)

| Criterion | Option A: per-request filter + cache | Option B: short-lived JWT rotated by heartbeat |
|---|---|---|
| Revocation latency | ≤ cache TTL (60 s), or 0 s on explicit invalidation | ≤ token TTL (e.g. 1 h) |
| Code changes | Filter + repo projection method + cache service | Rewrite of token emission, new refresh path in heartbeat, offline-device recovery logic |
| Failure mode | Cache down → DB hit per request (still functional, slower) | Heartbeat lost for > TTL → device locked out, needs human re-auth |
| Offline tolerance | Unaffected — token signature still valid, filter just re-checks | Breaks offline-first sync (a terminal losing connectivity for > TTL is kicked) |
| Infrastructure | `IMemoryCache` (already in stock ASP.NET Core) | Refresh-token state machine |
| Operational revocation UX | Admin click → next device request (≤ 60 s) fails 401 | Admin click → wait up to TTL |

**Decision: Option A** for this architecture. Option B's offline-locked-out failure mode is incompatible with the offline-first POS requirement baked into [`CLAUDE.md`](../CLAUDE.md) (orders persist to IndexedDB when the network is down). Option A preserves offline tolerance because the filter runs only when the device is online anyway. A secondary non-blocking recommendation — shortening the 10-year TTL to e.g. 90 days — is captured in §12 as defense in depth, not as part of this BDD.

---

## 3. Technical Requirements

### 3.1 Functional Requirements

| ID | Requirement | Acceptance Criteria |
|---|---|---|
| **FR-001** | `DeviceActiveAuthorizationFilter` gates every request whose JWT has `type=device` | When `Device.IsActive = false`, the filter returns `401 Unauthorized` before the action executes. Human-token requests (no `type=device` claim) pass through untouched. |
| **FR-002** | Cache layer for `IsActive` lookups | `IMemoryCache` with TTL = 60 s, keyed by `deviceId` (int). P95 of auth filter overhead < 2 ms on cache hit. |
| **FR-003** | `PATCH /api/devices/{id}/toggle-active` invalidates cache | The endpoint writes the DB flip and removes the cache entry for that `deviceId` in the same request. The next device call reflects the new state without waiting for TTL. |
| **FR-004** | `GET /api/devices` returns a list for the caller's business | Scoped by `BusinessId` claim. Accepts optional `?branchId=` filter. Returns an array of `DeviceListItemResponse`. |
| **FR-005** | `DeviceListItemResponse` DTO | Projects `Id`, `DeviceUuid`, `Name`, `Mode`, `IsActive`, `BranchId`, `BranchName`, `LastSeenAt`, `CreatedAt`. **Never** emits `DeviceToken`. |
| **FR-006** | Toggle is scoped to the caller's business | A Manager in business 42 cannot toggle a device in business 43 → `404 Not Found` (no disclosure of cross-tenant existence). |
| **FR-007** | Filter rejects device tokens with missing / malformed `deviceId` claim | `401 Unauthorized` with logged WARN. |
| **FR-008** | Existing endpoints that currently accept device tokens continue to work when `IsActive = true` | No regression on `PUT /heartbeat`, SignalR connections, KDS endpoints. |
| **FR-009** | `PATCH /api/devices/{id}` updates `Name` and/or `BranchId` within the caller's business | Request body accepts any subset of `{ name?, branchId? }`. Tenant guard identical to toggle: cross-business id → opaque 404. Providing an empty body → 400. The target `BranchId` (when provided) must belong to the caller's `BusinessId` — cross-business branch → 400/404 (see §6.x). |
| **FR-010** | SignalR hubs apply the same `IsActive` revocation check as MVC | A `DeviceActiveHubFilter` implementing `IHubFilter` runs on every hub method invocation and on connection establishment. When the incoming connection's JWT has `type=device` and `Device.IsActive = false` (or the device is missing), the filter throws `HubException("Device revoked")` for invocations and aborts the connection for connects. |

### 3.2 Non-Functional Requirements

- **Performance:**
  - Auth filter overhead ≤ 2 ms P95 on cache hit; ≤ 15 ms P95 on cache miss (single PK lookup on `Devices`).
  - `GET /api/devices` P95 < 80 ms for branches with ≤ 200 devices (realistic upper bound: one branch rarely has more than a handful).
  - No N+1 — `BranchName` arrives via a single `Include` or projection.
- **Security:**
  - Cross-tenant access must be impossible: `GET /api/devices?branchId=X` where branch belongs to another business → 200 with empty array or 403; choice documented in §6.
  - Device tokens never enter the Back Office management endpoints — those are `[Authorize(Roles="Owner,Manager")]` which require a **user** JWT (`sessionType=email|pin`), not a device JWT.
  - Cache is process-local (`IMemoryCache`). In a multi-instance deployment, each instance's cache has up to 60 s of staleness independently; this is acceptable given the 60 s TTL is already the design SLA (see §12 for scale-out consideration).
- **Backward compatibility:**
  - No changes to token emission contract — the 10-year expiration stays. The filter is additive.
  - No changes to `DeviceResponse` — `DeviceListItemResponse` is a new sibling DTO.
  - No migrations — `Device.IsActive` already exists.

---

## 4. Architecture Design

### 4.1 Component Overview

| Component | Change |
|---|---|
| `POS.API/Filters/DeviceActiveAuthorizationFilter.cs` | **New.** `IAsyncAuthorizationFilter` that gates device tokens. |
| `POS.Services/IService/IDeviceAuthorizationService.cs` | **New.** Thin service wrapping the cache + repo call. Allows unit-testing the filter without MVC infrastructure. |
| `POS.Services/Service/DeviceAuthorizationService.cs` | **New.** Implementation with `IMemoryCache`. |
| `POS.Repository/IRepository/IDeviceRepository.cs` | **Modified.** Add `Task<bool?> GetIsActiveAsync(int deviceId)` returning null when the device does not exist. |
| `POS.Repository/Repository/DeviceRepository.cs` | **Modified.** Implement the new method with `AsNoTracking` + PK projection. |
| `POS.API/Program.cs` | **Modified.** Register the filter globally (so it runs for every MVC action automatically) and register `IDeviceAuthorizationService` + `IMemoryCache`. |
| `POS.API/Controllers/DevicesController.cs` | **Modified.** Add `GET /api/devices`, `PATCH /api/devices/{id}/toggle-active`, and `PATCH /api/devices/{id}`. |
| `POS.Services/IService/IDeviceService.cs` | **Modified.** Add `ListForBusinessAsync(int businessId, int? branchId)`, `ToggleActiveAsync(int deviceId, int callerBusinessId)`, and `UpdateDeviceAsync(int deviceId, int callerBusinessId, UpdateDeviceRequest request)`. |
| `POS.Services/Service/DeviceService.cs` | **Modified.** Implement all three. `ToggleActiveAsync` and `UpdateDeviceAsync` must invalidate the auth cache for the affected `deviceId` (a branch change does not alter revocation but keeps the invariant that "any admin mutation on a device flushes its cache"). |
| `POS.Domain/DTOs/Device/DeviceListItemResponse.cs` | **New.** List projection DTO. |
| `POS.Domain/DTOs/Device/UpdateDeviceRequest.cs` | **New.** Partial-update DTO with optional `Name` and `BranchId`. |
| `POS.API/Filters/DeviceActiveHubFilter.cs` | **New.** `IHubFilter` that applies the same `IsActive` gate to SignalR invocations and connections. |

### 4.2 Data Flow — Incoming Device-Token Request

1. Request arrives at any MVC action.
2. JWT Bearer middleware validates signature + expiration → attaches `ClaimsPrincipal`.
3. `DeviceActiveAuthorizationFilter.OnAuthorizationAsync` runs (global filter).
4. Filter reads `type` claim. If absent or ≠ `"device"` → return (no-op; human token).
5. Filter reads `deviceId` claim. If absent or non-integer → set `context.Result = 401` and log WARN. Return.
6. Filter calls `IDeviceAuthorizationService.IsDeviceActiveAsync(deviceId)`.
7. Service checks `IMemoryCache` for key `"device:active:{deviceId}"`:
   - Hit → return cached bool.
   - Miss → call `IDeviceRepository.GetIsActiveAsync(deviceId)`, cache the result with `AbsoluteExpirationRelativeToNow = 60 s`, return.
8. If result is `null` (device deleted) or `false` (revoked) → filter sets `context.Result = 401`, logs INFO.
9. Otherwise → filter returns, action executes normally.

### 4.3 Data Flow — `PATCH /api/devices/{id}/toggle-active`

1. Controller action (`[Authorize(Roles="Owner,Manager")]`) receives the request.
2. Reads `BusinessId` from user JWT (via `BaseApiController.BusinessId`).
3. Calls `IDeviceService.ToggleActiveAsync(id, businessId)`.
4. Service loads `Device` by id.
5. **Tenant guard:** if `device == null` OR `device.BusinessId != callerBusinessId` → throw `NotFoundException` (opaque 404; never 403, to avoid tenant enumeration).
6. Service flips `device.IsActive`, sets `device.LastSeenAt` untouched.
7. `SaveChangesAsync`.
8. Service calls `IDeviceAuthorizationService.InvalidateAsync(deviceId)` → removes the cache entry on this process.
9. Service returns the new `IsActive` value in a thin DTO `{ id, isActive }`.
10. Controller returns `200 OK` with the DTO.

### 4.4 Data Flow — `GET /api/devices?branchId=?`

1. Controller action (`[Authorize(Roles="Owner,Manager")]`) receives the request.
2. Reads `BusinessId` from user JWT.
3. Calls `IDeviceService.ListForBusinessAsync(businessId, branchId)`.
4. Service queries `Devices` with:
   - `.Where(d => d.BusinessId == businessId)`
   - Conditional `.Where(d => d.BranchId == branchId)` when the filter is present.
   - `.Include(d => d.Branch)` so `Branch.Name` is loaded in the same roundtrip.
   - `.OrderBy(d => d.BranchId).ThenBy(d => d.Name)` for deterministic UI.
   - `.Select(d => new DeviceListItemResponse { ... })` projection.
5. Returns `IEnumerable<DeviceListItemResponse>` (no pagination in v1; see §11).

### 4.5 Database Schema Changes

**None.** Every required field (`IsActive`, `LastSeenAt`, `BranchId`, `BusinessId`, `Name`, `Mode`) already exists on `Device`. `Branch.Name` is already persisted. No migration.

---

## 5. API Contract

### 5.1 New Endpoints

#### `GET /api/devices`

| Aspect | Value |
|---|---|
| Auth | `[Authorize(Roles="Owner,Manager")]` (user JWT only; the global filter still applies but passes because `type != device`) |
| Query params | `branchId?: int` |
| Response 200 | `DeviceListItemResponse[]` |
| Response 400 | Query `branchId` present but non-integer (handled by model binding) |
| Response 401 | Missing/invalid user JWT |
| Response 403 | JWT valid but role ∉ {Owner, Manager} |

Example response:
```json
[
  {
    "id": 12,
    "deviceUuid": "a1b2c3d4-...",
    "name": "Caja 1",
    "mode": "cashier",
    "isActive": true,
    "branchId": 3,
    "branchName": "Sucursal Centro",
    "lastSeenAt": "2026-04-20T18:05:12.0000000Z",
    "createdAt": "2026-03-14T12:00:00.0000000Z"
  }
]
```

#### `PATCH /api/devices/{id}/toggle-active`

| Aspect | Value |
|---|---|
| Auth | `[Authorize(Roles="Owner,Manager")]` |
| Path param | `id: int` |
| Request body | none |
| Response 200 | `{ id: int, isActive: bool }` |
| Response 401 | Missing/invalid user JWT |
| Response 403 | Role not permitted |
| Response 404 | Device does not exist OR belongs to another business (opaque — see §6.3) |

#### `PATCH /api/devices/{id}` — rename and/or reassign branch

| Aspect | Value |
|---|---|
| Auth | `[Authorize(Roles="Owner,Manager")]` |
| Path param | `id: int` |
| Request body | `UpdateDeviceRequest { name?: string, branchId?: int }` — both optional, at least one required |
| Response 200 | `DeviceListItemResponse` (same shape as list items; includes refreshed `BranchName`) |
| Response 400 | Empty body (neither field provided), `name` exceeds `MaxLength(100)`, or `branchId` belongs to another business |
| Response 401 | Missing/invalid user JWT |
| Response 403 | Role not permitted |
| Response 404 | Device does not exist OR belongs to another business (opaque — see §6.3) |

Partial-update semantics: fields omitted from the request body are left untouched. Example bodies:

```json
{ "name": "Caja 2" }                  // rename only
{ "branchId": 5 }                     // reassign branch only
{ "name": "Caja 2", "branchId": 5 }   // both in one atomic operation
```

**Why a full `DeviceListItemResponse` on 200 instead of just the mutated fields:** the Back Office list UI can splice the response back into the grid without a re-fetch. The `Branch.Name` projection is already available via the repo path reused from `GET /api/devices`. The shape is stable and cacheable.

### 5.2 New DTO — `DeviceListItemResponse`

Lives in `POS.Domain/DTOs/Device/`.

| Field | Type | Source |
|---|---|---|
| `Id` | `int` | `Device.Id` |
| `DeviceUuid` | `string` | `Device.DeviceUuid` |
| `Name` | `string?` | `Device.Name` |
| `Mode` | `string` | `Device.Mode` |
| `IsActive` | `bool` | `Device.IsActive` |
| `BranchId` | `int` | `Device.BranchId` |
| `BranchName` | `string` | `Device.Branch.Name` (via Include) |
| `LastSeenAt` | `DateTime?` | `Device.LastSeenAt` |
| `CreatedAt` | `DateTime` | `Device.CreatedAt` |

**Never** emits `DeviceToken`. The JWT is only returned at `/register` time; re-exposing it on the admin list would be a token-leakage vector.

### 5.3 New DTO — `UpdateDeviceRequest`

Lives in `POS.Domain/DTOs/Device/`.

| Field | Type | Validation | Notes |
|---|---|---|---|
| `Name` | `string?` | `[MaxLength(100)]` | Optional. When present, trimmed before persisting. Empty-after-trim rejected with 400. |
| `BranchId` | `int?` | Must belong to caller's business | Optional. When present, target branch must be active and scoped to caller's `BusinessId`. |

At least one field must be present — an empty body is rejected with `400 Bad Request`. This guard is enforced at the service layer (not via model-binding) so the failure path is consistent with other tenant-safety checks.

### 5.4 Service Interface Changes

`IDeviceService` (conceptual additions):

```
Task<IReadOnlyList<DeviceListItemResponse>> ListForBusinessAsync(int businessId, int? branchId);
Task<(int id, bool isActive)> ToggleActiveAsync(int deviceId, int callerBusinessId);
Task<DeviceListItemResponse> UpdateDeviceAsync(int deviceId, int callerBusinessId, UpdateDeviceRequest request);
  // Partial update. Null fields are not touched. Throws ValidationException on empty body,
  // NotFoundException on cross-tenant or missing device, ValidationException on cross-tenant branch.
```

`IDeviceRepository` (conceptual additions):

```
Task<bool?> GetIsActiveAsync(int deviceId);
  // null when the device does not exist; bool otherwise.
Task<IReadOnlyList<DeviceListItemResponse>> ListProjectedAsync(int businessId, int? branchId);
  // Projects in SQL with Branch.Name via Include; no entity tracking.
```

`IDeviceAuthorizationService` (new):

```
Task<bool?> IsDeviceActiveAsync(int deviceId);
  // Null when the device does not exist; bool otherwise.
Task InvalidateAsync(int deviceId);
  // Removes the IMemoryCache entry so the next lookup re-reads from DB.
```

### 5.5 MVC Filter Wiring

Global registration in `Program.cs` via `builder.Services.AddControllers(opt => opt.Filters.Add<DeviceActiveAuthorizationFilter>())` so the filter applies to every MVC action without per-controller attributes.

### 5.6 SignalR Hub Filter — `DeviceActiveHubFilter`

`POS.API/Filters/DeviceActiveHubFilter.cs` implements `Microsoft.AspNetCore.SignalR.IHubFilter`. It mirrors the MVC filter logic but plugs into SignalR's pipeline so hub invocations and hub lifetime events enforce the same `IsActive` gate.

| Aspect | Value |
|---|---|
| Interface | `Microsoft.AspNetCore.SignalR.IHubFilter` |
| Overrides | `OnConnectedAsync(HubLifetimeContext, Func<HubLifetimeContext, Task>)` and `InvokeMethodAsync(HubInvocationContext, Func<HubInvocationContext, ValueTask<object?>>)` |
| Dependency | `IDeviceAuthorizationService` (same instance shape the MVC filter uses) |
| Connection failure mode | Throws `HubException("Device revoked")` from `OnConnectedAsync`. SignalR closes the transport with an `error` frame; the client observes a failed negotiation and does not retry unless it mints a new token. |
| Invocation failure mode | Throws `HubException("Device revoked")` from `InvokeMethodAsync`. The invocation return propagates the exception message to the client; the connection itself is not torn down (SignalR default behavior), but every subsequent invocation hits the same gate. |

**Wiring in `Program.cs`:**

```
builder.Services.AddSignalR(opt => opt.AddFilter<DeviceActiveHubFilter>());
```

Global registration means every hub defined in the app (current: `KdsHub` and any future ones) inherits the gate without per-hub opt-in. Human tokens bypass the check — same discriminator as the MVC filter (§6.1).

**Why throw instead of drop silently on invocation:** an invocation error surfaces in the client's catch block, letting the UI display "access revoked" or trigger a forced logout. A silent drop would look like a transient network error and trigger reconnection loops.

---

## 6. Business Logic Specifications

### 6.1 Filter Algorithm

```
function OnAuthorizationAsync(context)
    user := context.HttpContext.User
    if user is not authenticated: return  // bearer middleware already 401'd or endpoint is anonymous
    typeClaim := user.FindFirst("type")?.Value
    if typeClaim != "device": return       // human tokens ignored by this filter

    deviceIdClaim := user.FindFirst("deviceId")?.Value
    if not int.TryParse(deviceIdClaim): set 401; return

    isActive := await deviceAuthService.IsDeviceActiveAsync(deviceId)
    if isActive is null or isActive == false:
        set 401; log INFO "device revoked" with deviceId; return

    // active device — pass through
```

### 6.2 Cache Behavior

```
function IsDeviceActiveAsync(deviceId)
    key := $"device:active:{deviceId}"
    if cache.TryGet(key, out bool? cached):
        return cached
    fromDb := await repo.GetIsActiveAsync(deviceId)  // nullable bool
    cache.Set(key, fromDb, AbsoluteExpirationRelativeToNow = 60s)
    return fromDb

function InvalidateAsync(deviceId)
    cache.Remove($"device:active:{deviceId}")
```

**Negative caching:** `null` (device not found) and `false` (revoked) are both cached for the same 60 s TTL. This is safe: if someone recreates a device with the same id (not possible — ids are identity), or flips a flag, explicit invalidation kicks in via `ToggleActiveAsync`.

### 6.3 Toggle — Tenant Guard

```
function ToggleActiveAsync(deviceId, callerBusinessId)
    device := await repo.GetByIdAsync(deviceId)
    if device == null or device.BusinessId != callerBusinessId:
        throw NotFoundException("Device not found")   // opaque — never 403
    device.IsActive := not device.IsActive
    await uow.SaveChangesAsync()
    await deviceAuthService.InvalidateAsync(deviceId)
    return (device.Id, device.IsActive)
```

**Why opaque 404 instead of 403:** a 403 when the device exists but belongs to another business leaks the existence of the id to a cross-tenant attacker. 404 for both "does not exist" and "wrong tenant" closes the enumeration hole.

### 6.4 Update — Partial Patch with Tenant Guard

```
function UpdateDeviceAsync(deviceId, callerBusinessId, request)
    if request.Name is null and request.BranchId is null:
        throw ValidationException("At least one of name or branchId must be provided")

    device := await repo.GetByIdAsync(deviceId)
    if device == null or device.BusinessId != callerBusinessId:
        throw NotFoundException("Device not found")   // opaque — same as toggle

    if request.Name is not null:
        trimmed := request.Name.Trim()
        if trimmed is empty:
            throw ValidationException("Name cannot be blank")
        device.Name := trimmed

    if request.BranchId is not null and request.BranchId != device.BranchId:
        targetBranch := await branchRepo.GetByIdAsync(request.BranchId)
        if targetBranch == null or targetBranch.BusinessId != callerBusinessId or not targetBranch.IsActive:
            throw ValidationException("BranchId is not a valid active branch in this business")
        device.BranchId := request.BranchId

    await uow.SaveChangesAsync()
    await deviceAuthService.InvalidateAsync(deviceId)   // flush cache after any admin mutation

    return await repo.GetProjectedByIdAsync(deviceId)  // returns DeviceListItemResponse
```

**Branch validation rationale:** allowing a reassignment to an inactive branch or a cross-business branch would either orphan the device operationally (inactive) or breach tenancy (cross-business). Both fail with **400** (not 404) because the *branch* id is user input from the body, not an enumerable admin target — disclosing "branch does not exist in your business" is acceptable because the caller already has a list of their own branches. Cross-tenant *device* ids remain 404 (path param is enumerable).

**Cache invalidation on branch change:** the cache only stores `IsActive`, not `BranchId`, so technically a branch change does not need invalidation for the revocation gate. The invalidation happens anyway to preserve the operational invariant "every admin mutation flushes the device's cache entry" — this avoids subtle bugs if the cache schema is later extended.

### 6.5 SignalR Hub Filter Algorithm

```
class DeviceActiveHubFilter : IHubFilter
    async InvokeMethodAsync(invocationContext, next):
        await GateAsync(invocationContext.Context.User, invocationContext.HubMethodName)
        return await next(invocationContext)

    async OnConnectedAsync(lifetimeContext, next):
        await GateAsync(lifetimeContext.Context.User, hubMethod: null)
        await next(lifetimeContext)

    private async GateAsync(user, hubMethod):
        if user is null or not user.Identity.IsAuthenticated: return
        typeClaim := user.FindFirst("type")?.Value
        if typeClaim != "device": return     // human tokens bypass

        deviceIdClaim := user.FindFirst("deviceId")?.Value
        if not int.TryParse(deviceIdClaim):
            throw HubException("Invalid device token")

        isActive := await deviceAuthService.IsDeviceActiveAsync(deviceId)
        if isActive is null or isActive == false:
            log INFO "device revoked via hub filter" with deviceId and hubMethod
            throw HubException("Device revoked")
```

The filter re-uses the same `IDeviceAuthorizationService` singleton the MVC filter uses, which means they share the `IMemoryCache` — a revocation invalidated once (by `ToggleActiveAsync`) is reflected on the next MVC request **and** the next hub invocation simultaneously.

### 6.6 List Query

```
function ListForBusinessAsync(businessId, branchId?)
    query := ctx.Devices.AsNoTracking().Where(d => d.BusinessId == businessId)
    if branchId has value: query := query.Where(d => d.BranchId == branchId)
    return await query
        .Include(d => d.Branch)
        .OrderBy(d => d.BranchId).ThenBy(d => d.Name)
        .Select(d => new DeviceListItemResponse { ..., BranchName = d.Branch.Name })
        .ToListAsync()
```

`BusinessId` filter is always applied; `branchId` is additive. A caller asking for `?branchId=X` where X belongs to another business returns an **empty array** (because `BusinessId` filter still scopes it away). No 403, no 404 — same non-enumeration principle.

### 6.7 Validation Rules

| ID | Rule | Error |
|---|---|---|
| **VR-001** | Device JWT without `deviceId` claim | 401 via filter |
| **VR-002** | Device JWT with `deviceId` claim that doesn't exist in DB | 401 via filter |
| **VR-003** | Device JWT with `deviceId` that exists but `IsActive = false` | 401 via filter |
| **VR-004** | Toggle target not owned by caller's business | 404 via service |
| **VR-005** | `branchId` query param that is not an int | 400 via model binding |
| **VR-006** | `PATCH /api/devices/{id}` with empty body (no `name`, no `branchId`) | 400 `"At least one of name or branchId must be provided"` |
| **VR-007** | `PATCH /api/devices/{id}` with `name` that is blank after trim | 400 `"Name cannot be blank"` |
| **VR-008** | `PATCH /api/devices/{id}` with `branchId` that is inactive or cross-business | 400 `"BranchId is not a valid active branch in this business"` |
| **VR-009** | SignalR connection/invocation by revoked device | `HubException("Device revoked")` propagated to client |

### 6.8 Edge Cases

| Case | Behavior |
|---|---|
| Device token presented to a `[AllowAnonymous]` endpoint | Filter still runs (global), but since anonymous endpoints don't require auth, the filter must not 401 the request. Handled by checking `user.Identity.IsAuthenticated` at the top — if false, return immediately without inspecting claims. |
| Admin toggles `IsActive = true` after disabling | Cache invalidation makes the re-enable effective within the same request cycle. |
| Clock skew between cache entry and DB flip | Worst case 60 s of stale allow/deny; acceptable given the design SLA. Explicit invalidation on toggle brings it to 0 s on the toggling process. |
| Multi-process deployment with in-memory cache | Each process has an independent 60 s window of staleness. See §12 for scale-out note. |
| Cache service failure | Filter falls through to the DB repo directly — slower (15 ms P95 per request) but still functional. No hard dependency on cache. |
| Device deleted while its token is still valid | Filter treats `null` as revoked → 401. No disclosure. |
| Filter runs on a SignalR negotiation | `DeviceActiveHubFilter` handles this on the SignalR pipeline (see §6.5). The MVC filter does not interfere with hub connections because hub negotiation is routed through SignalR's own authorization path. |
| `PATCH /api/devices/{id}` with `branchId` equal to the current branch | No-op branch-wise; `Name` update (if any) still applies. No ValidationException. |
| `PATCH` reassigns a device whose mode requires the new branch to support it (e.g. `tables` → retail branch) | **Not validated by this BDD.** The admin is trusted to move the device only between compatible branches. A future BDD may add macro↔mode coherence checks (flagged in §11 follow-ups). |
| Device already revoked, admin `PATCH`es it | The update succeeds (name/branch change). `IsActive` is untouched. The device remains revoked; admin must also call toggle to re-enable. |
| Hub filter sees a human user JWT | Returns without inspecting — human tokens are out of this gate's responsibility. |
| Hub invocation mid-flight when admin flips `IsActive=false` | The *current* invocation completes; the *next* invocation hits the gate and throws `HubException`. The connection itself is not torn down until the client reconnects. |

---

## 7. Performance Optimization Strategy

### 7.1 Query Optimization

- `IDeviceRepository.GetIsActiveAsync`: single-column projection — `ctx.Devices.AsNoTracking().Where(d => d.Id == id).Select(d => (bool?)d.IsActive).FirstOrDefaultAsync()`. Hits the PK index, ≤ 1 ms on hot path.
- `IDeviceRepository.ListProjectedAsync`: single `SELECT ... INNER JOIN Branches ON ...` via `Include + Select`. Existing index `IX_Devices_BusinessId` (already scaffolded per [AUDIT-011 Phase 1](AUDIT-011-backend-roles-devices.md)) serves the predicate; if `branchId` filter is applied, the composite is still selective enough for a range scan.

### 7.2 Caching Strategy

- Cache: `IMemoryCache`, process-local.
- Key: `"device:active:{deviceId}"`.
- TTL: 60 s `AbsoluteExpirationRelativeToNow`. No sliding expiration.
- Value: `bool?` (`null` = not found, `true` = active, `false` = revoked).
- Invalidation triggers:
  - Explicit `Remove` on `ToggleActiveAsync`.
  - Implicit TTL expiry on every other path.
- Cache is **not** shared across processes. In single-node deployment (current), behavior is identical to global cache. In scale-out, each instance re-reads independently — acceptable since the TTL is the design SLA.

### 7.3 No Bulk Operations

Filter runs once per request; toggle acts on a single row. No bulk semantics.

---

## 8. Error Handling Strategy

| Scenario | Exception | HTTP Status | Logging |
|---|---|---|---|
| Device token with missing `deviceId` | — (short-circuit by filter) | 401 | WARN with remote IP |
| Device revoked (IsActive=false) | — (short-circuit by filter) | 401 | INFO with `deviceId` |
| Toggle target not found / cross-tenant | `NotFoundException` | 404 | INFO |
| List caller has no JWT | (bearer middleware) | 401 | — |
| List caller wrong role | (authorize filter) | 403 | — |
| DB transient failure during filter lookup | Propagates | 500 | ERROR; filter does NOT mask DB failures as 401 — that would mask outages |

---

## 9. Testing Requirements

### 9.1 Unit Tests — Filter

- Human token (no `type` claim): filter does not invoke `IDeviceAuthorizationService`, request proceeds.
- Device token, `IsActive = true`: request proceeds.
- Device token, `IsActive = false`: 401.
- Device token, `deviceId` claim missing: 401 + WARN logged.
- Device token, `deviceId` claim is `"abc"` (non-integer): 401 + WARN logged.
- Anonymous request (no JWT at all): filter returns without inspecting claims.
- Second request within 60 s after toggle: cache invalidation guarantees 401 (not cached 200).

### 9.2 Unit Tests — Service

- `ListForBusinessAsync` scopes by `BusinessId` and returns projected DTOs including `BranchName`.
- `ListForBusinessAsync` with `branchId` filter combines predicates correctly.
- `ListForBusinessAsync` with `branchId` of another business returns empty array.
- `ToggleActiveAsync` flips the bit and invalidates cache.
- `ToggleActiveAsync` with cross-tenant id throws `NotFoundException`.
- `ToggleActiveAsync` on deleted device throws `NotFoundException`.
- `UpdateDeviceAsync` with both `name` and `branchId` updates atomically and invalidates cache.
- `UpdateDeviceAsync` with only `name` leaves `BranchId` untouched.
- `UpdateDeviceAsync` with only `branchId` leaves `Name` untouched.
- `UpdateDeviceAsync` with neither field throws `ValidationException` (empty body guard).
- `UpdateDeviceAsync` with blank-after-trim `name` throws `ValidationException`.
- `UpdateDeviceAsync` with `branchId` from another business throws `ValidationException`.
- `UpdateDeviceAsync` with inactive `branchId` throws `ValidationException`.
- `UpdateDeviceAsync` with cross-tenant device id throws `NotFoundException`.

### 9.3 Unit Tests — SignalR Hub Filter

- Human token connecting to a hub: `OnConnectedAsync` proceeds, no `IsDeviceActiveAsync` call.
- Device token with `IsActive=true`: connection proceeds; invocations succeed.
- Device token with `IsActive=false`: `OnConnectedAsync` throws `HubException("Device revoked")`.
- Device token with `IsActive=true` at connect but revoked mid-session: next invocation throws `HubException`; cache invalidation propagates from `ToggleActiveAsync`.
- Device token missing `deviceId` claim: `OnConnectedAsync` throws `HubException("Invalid device token")`.
- Anonymous connection (no JWT): filter returns without inspecting — hub's own `[Authorize]` decides.

### 9.4 Integration Tests

- Full flow: register device → call protected endpoint (200) → admin `PATCH /toggle-active` (200) → same device token calls protected endpoint within 500 ms → 401.
- `GET /api/devices` as Owner returns all devices in business; `?branchId=X` filters correctly.
- `GET /api/devices` with user token from a different business returns only that business's devices.
- `PATCH /toggle-active` with device token (not admin) → 403.
- `PATCH /toggle-active` on another business's device id → 404.
- `PATCH /api/devices/{id}` with `{ name: "Caja 2" }` as Owner → 200, list-shape response with renamed device.
- `PATCH /api/devices/{id}` with `{ branchId: <own business branch> }` → 200, `BranchName` reflects new branch.
- `PATCH /api/devices/{id}` with empty body `{}` → 400.
- `PATCH /api/devices/{id}` with `{ branchId: <another business branch> }` → 400 (branch validation).
- `PATCH /api/devices/{id}` on another business's device → 404 (opaque).
- SignalR: active device opens hub connection → succeeds; invocations succeed.
- SignalR: admin toggles device off → next hub invocation throws `HubException`.
- SignalR: revoked device tries to reconnect → `OnConnectedAsync` throws, transport closes.

### 9.5 Performance Tests

- Filter adds ≤ 2 ms P95 on cache hit at 100 RPS sustained.
- Filter adds ≤ 15 ms P95 on cache miss at 100 RPS sustained.
- `GET /api/devices` P95 < 80 ms with 200 devices across 10 branches.

---

## 10. Implementation Phases

| Phase | Scope | Dependencies | Complexity |
|---|---|---|---|
| **Phase 1** | Add `IDeviceRepository.GetIsActiveAsync` + impl (single-column projection). | None | **Low** |
| **Phase 2** | Add `IDeviceAuthorizationService` + impl with `IMemoryCache`; register in DI. | Phase 1 | **Low** |
| **Phase 3** | Add `DeviceActiveAuthorizationFilter`; register globally in `Program.cs`. | Phase 2 | **Medium** |
| **Phase 4** | Add `DeviceListItemResponse` DTO; add `ListProjectedAsync` to repo (also exposing `GetProjectedByIdAsync` for single-item projection used by PATCH); add `ListForBusinessAsync` to service; add `GET /api/devices` action. | None (parallel to 1–3) | **Low** |
| **Phase 5** | Add `ToggleActiveAsync` to service (with cache invalidation); add `PATCH /api/devices/{id}/toggle-active` action. | Phases 2 + 4 | **Low** |
| **Phase 6** | Add `UpdateDeviceRequest` DTO; add `UpdateDeviceAsync` to service (partial-update + branch validation + cache invalidation); add `PATCH /api/devices/{id}` action returning `DeviceListItemResponse`. | Phases 2 + 4 | **Medium** |
| **Phase 7** | Add `DeviceActiveHubFilter` implementing `IHubFilter`; register globally via `AddSignalR(opt => opt.AddFilter<...>())`. Verify the existing KDS hub honors the gate. | Phase 2 | **Medium** |
| **Phase 8** | Wire up the existing orphan `GetByBranchAsync` or deprecate it (no orphaned repo methods on the merged branch). | Phase 4 | **Low** |

Phases 1 → 2 → 3 are strictly sequential. Phase 4 may ship in parallel with 1–3. Phase 5 depends on both 2 and 4.

---

## 11. Out of Scope

- **Multi-instance cache coherence.** If the deployment becomes horizontally scaled, swap `IMemoryCache` for a distributed `IDistributedCache` (Redis). Design unchanged; only the cache implementation swaps.
- **Token lifetime reduction.** Shortening the 10-year expiration to 90 days is a recommended hardening but orthogonal to the revocation mechanism designed here. Tracked separately.
- **Macro↔mode coherence check on branch reassignment.** `PATCH /api/devices/{id}` with `branchId` does not verify that the new branch's macro category supports the device's current `Mode` (e.g. moving a `tables`-mode device to a `Retail` branch). Admin is trusted. Tracked as follow-up.
- **Activation-code listing / cancellation.** Distinct problem domain.
- **Audit log of who toggled/renamed/reassigned what and when.** Not in scope; `Device.LastSeenAt` is not an audit field.
- **Pagination on `GET /api/devices`.** Realistic device counts per business are in the low tens. Skip pagination until evidence shows > 200.
- **Frontend work.** This BDD covers backend only. AUDIT-032 maps the greenfield frontend work.
- **Full `DELETE /api/devices/{id}`.** Soft-delete via `toggle-active` already covers operational revocation; a hard delete cascading through `CashRegister`, `DeviceActivationCode` traceability links, and any `PrintJob` history is a separate decision.

---

## 12. Security Considerations

- **Revocation latency.** With cache TTL 60 s and explicit invalidation on toggle, a revoked device is locked out within one request cycle on the toggling process and within 60 s across the fleet. This is an explicit SLA, documented in §7.2.
- **Cross-tenant enumeration.** All admin endpoints return 404 (not 403) on cross-tenant targets; `GET /api/devices` filters by `BusinessId` server-side — a client-chosen `branchId` from another business yields an empty array, not an error.
- **Token leakage.** `DeviceListItemResponse` never includes `DeviceToken`. Only `POST /api/devices/register` returns a token, and only to the device itself.
- **Cache poisoning.** `IMemoryCache` is process-local and keyed by an integer id under our control — no external input reaches the key. Negative caching is bounded by the same 60 s TTL.
- **DoS via cache miss storm.** If an attacker flips `IsActive` repeatedly, each flip invalidates the cache; the next request refills it. Worst case: 1 DB roundtrip per flip-request pair. Rate-limited by the existing admin-endpoint policies.
- **Scale-out caveat.** If this deploys behind multiple app servers, each process has its own 60 s stale window. For lower bounds, promote `IMemoryCache` to `IDistributedCache` (Redis) — the service interface is unchanged; only the implementation swaps.
- **Defense in depth (recommendation, not in scope).** Reducing the device JWT lifetime from 10 years to a bounded value (e.g. 90 days) adds a second layer of protection should the filter ever fail open. This is a token-emission change in `AuthService.GenerateDeviceToken` and should ship as a separate BDD to preserve a clean rollback boundary.
