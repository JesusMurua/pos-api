# AUDIT-011: Roles vs. Devices — Architectural Gap Analysis

**Date:** 2026-04-07
**Scope:** POS.API backend — User/Role entities, Device tracking, JWT claims, provisioning endpoints
**Status:** Draft

---

## 1. Current State

### 1.1 User & Role System

| Aspect | Implementation | Location |
|--------|---------------|----------|
| **Role definition** | `UserRole` enum: `Owner`, `Manager`, `Cashier`, `Kitchen`, `Waiter`, `Kiosk`, `Host` | `POS.Domain/Enums/UserRole.cs` |
| **Storage** | String column `[MaxLength(20)]` with `HasConversion<string>()` | `ApplicationDbContext.cs:196` |
| **Assignment** | Single role per user (`User.Role`), default `Cashier` | `POS.Domain/Models/User.cs` |
| **Multi-branch** | `UserBranch` join table with `IsDefault` flag for JWT generation | `POS.Domain/Models/UserBranch.cs` |
| **Catalog** | `UserRoleCatalog` system table for UI metadata | `POS.Domain/Models/Catalogs/UserRoleCatalog.cs` |

### 1.2 Device Tracking

There is **no first-class `Device` entity**. Device identity is spread across two unrelated concepts:

| Concept | Entity | Purpose |
|---------|--------|---------|
| **Cash drawer** | `CashRegister` | Has optional `DeviceUuid` field, tracks till hardware | `POS.Domain/Models/CashRegister.cs` |
| **Activation code** | `DeviceActivationCode` | 6-digit code with `Mode` string (cashier/kiosk/tables/kitchen), 24h TTL | `POS.Domain/Models/DeviceActivationCode.cs` |
| **Mode catalog** | `DeviceModeCatalog` | Reference table: `cashier`, `kiosk`, `tables`, `kitchen` | Seeded in `DbInitializer.cs:99-108` |

**Key observation:** `DeviceActivationCode.Mode` is consumed at activation time but **never persisted to a device record**. After the code is redeemed, the backend forgets which mode was assigned.

### 1.3 Authentication & JWT

**Login flows:**

| Flow | Endpoint | Audience | Token TTL |
|------|----------|----------|-----------|
| Email + password | `POST /api/auth/email-login` | Owner, Manager | 30 days |
| PIN | `POST /api/auth/pin-login` | Cashier, Kitchen, Waiter, Kiosk, Host | 12 hours |
| Branch switch | `POST /api/auth/switch-branch` | All authenticated | Re-issued |

**JWT claims (10 total):**

```
NameIdentifier (userId), Role, Name, businessId, branchId,
branches (JSON), planType, businessType, trialEndsAt, onboardingCompleted
```

**Missing from token:** `deviceId`, `deviceUuid`, `deviceMode`.

### 1.4 Device Provisioning Flow

Three-step process in `DeviceController`:

| Step | Endpoint | Auth | What happens |
|------|----------|------|-------------|
| 1. Generate code | `POST /api/device/generate-code` | Owner, Manager | Creates `DeviceActivationCode` with mode + 24h expiry |
| 2. Activate | `POST /api/device/activate` | Anonymous | Validates code, marks as used, returns `{businessId, branchId, mode}` |
| 3. Setup | `POST /api/device/setup` | Anonymous | Owner email+password → returns business info + branches |

**After these 3 steps, the device has NO persistent identity in the backend.** The frontend receives the mode and business context, but the backend does not record "Device X is a KDS at Branch Y."

---

## 2. Architectural Gaps

### GAP-1: No `Device` Entity — Backend Is Blind to Physical Devices

**Problem:** The backend has no table that says "Device `abc-123` is a KDS assigned to Branch 2." The `CashRegister.DeviceUuid` field only covers cash-drawer hardware — it does not represent a KDS, Kiosk, or Tables terminal.

**Impact:**
- Cannot revoke or reconfigure a device remotely.
- Cannot audit which devices are active per branch.
- Cannot enforce "this device is a KDS and may only call KDS endpoints."

### GAP-2: Activation Code Mode Is Fire-and-Forget

**Problem:** `DeviceActivationCode` stores the intended `Mode`, but once the code is redeemed, the mode is only returned in the HTTP response — it is never written to a persistent device record.

**Impact:**
- The frontend stores mode in `localStorage` — a user can change it with DevTools.
- If the device clears storage, the mode is lost and there is no backend source of truth to recover from.

### GAP-3: JWT Contains No Device Context

**Problem:** The token identifies who (user + role) and where (branch), but not what (device + mode). Every API call from a KDS looks identical to one from a POS.

**Impact:**
- Cannot build endpoint-level authorization rules like "only KDS devices may call `PATCH /api/orders/{id}/kitchen-status`."
- Audit logs cannot distinguish actions by device.

### GAP-4: Role Enum Conflates Human Roles with Device Modes

**Problem:** The `UserRole` enum includes `Kiosk` and `Kitchen`. These are device modes, not human roles. A person is a Cashier; a device is a Kiosk. Mixing them means:
- A "Kiosk user" exists in the `Users` table even though no human logs in at a kiosk.
- Role-based authorization (`[Authorize(Roles = "Kitchen")]`) conflates "is this a kitchen staff member?" with "is this request coming from a KDS?"

**Impact:**
- Cannot model "Cashier Juan logs in at a Kiosk device" — the system would need Juan to be role `Kiosk`, losing his actual role.
- Multi-role scenarios (waiter uses a tables device, then switches to POS) are impossible.

### GAP-5: No Device-Scoped Session or Token

**Problem:** After activation (Step 2), the device calls setup (Step 3) which is just an email/password login. The resulting JWT is a standard user token — there is no device-scoped token that locks the session to a specific device UUID + mode.

**Impact:**
- The same token can be used from any device.
- If a device token is leaked, it cannot be revoked independently from the user.

### GAP-6: CashRegister ≠ Device

**Problem:** `CashRegister` represents a logical cash drawer (name, branch, sessions, movements). It happens to have a `DeviceUuid` field, but it is not a generic device record. A KDS has no cash register. A Kiosk has no cash register.

**Impact:**
- Only POS-mode devices can be tracked via `CashRegister`.
- KDS and Kiosk devices have zero backend footprint after activation.

---

## 3. Implementation Plan

### Phase 1: Device Entity & Registration (Foundation)

**Step 1.1 — Create `Device` model**

```
POS.Domain/Models/Device.cs
```

| Field | Type | Description |
|-------|------|-------------|
| `Id` | `int` | PK |
| `BusinessId` | `int` | FK → Business |
| `BranchId` | `int` | FK → Branch |
| `DeviceUuid` | `string [100]` | Client-generated UUID, unique per business |
| `Name` | `string [50]` | Human-readable label ("Caja 1", "KDS Cocina") |
| `Mode` | `string [20]` | Locked mode: cashier / kiosk / tables / kitchen |
| `IsActive` | `bool` | Can be remotely disabled |
| `ActivatedAt` | `DateTime` | When device was provisioned |
| `LastSeenAt` | `DateTime?` | Heartbeat timestamp |
| `ActivationCodeId` | `int?` | FK → DeviceActivationCode (traceability) |
| `CashRegisterId` | `int?` | FK → CashRegister (only for mode=cashier) |

**Unique constraint:** `(BusinessId, DeviceUuid)`

**Step 1.2 — Migration & DbContext**

- Add `DbSet<Device>` to `ApplicationDbContext`.
- Create migration `AddDeviceEntity`.
- Seed nothing — devices are created at activation time.

**Step 1.3 — Refactor Activation Flow**

Modify `DeviceService.ValidateActivationCodeAsync` to:

1. Accept `deviceUuid` in the `ActivateDeviceRequest`.
2. Create a `Device` record with mode from the activation code.
3. Return `DeviceId` in the response.

New flow:

```
POST /api/device/activate { code: "123456", deviceUuid: "abc-..." }
→ Creates Device record
→ Returns { deviceId, businessId, branchId, mode, ... }
```

### Phase 2: Device-Aware Authentication

**Step 2.1 — Device Token (new endpoint)**

```
POST /api/auth/device-login
Body: { deviceUuid, branchId, pin? }  // PIN optional for kiosk mode
```

Service logic:
- Look up `Device` by UUID + branchId → verify `IsActive`.
- If mode requires user (cashier, tables, waiter): validate PIN, resolve User.
- If mode is unattended (kiosk): issue token without user identity.
- Update `Device.LastSeenAt`.

**Step 2.2 — Add Device Claims to JWT**

Add to `GenerateJwtToken`:

```csharp
new Claim("deviceId", device.Id.ToString())
new Claim("deviceUuid", device.DeviceUuid)
new Claim("deviceMode", device.Mode)
```

**Step 2.3 — Create `RequiresDeviceMode` Authorization Attribute**

```csharp
[RequiresDeviceMode("kitchen")]  // Only KDS devices
public async Task<IActionResult> UpdateKitchenStatus(...)
```

Handler reads `deviceMode` claim, rejects if mismatch.

### Phase 3: Clean Up Role Enum

**Step 3.1 — Remove device-mode entries from `UserRole`**

Remove `Kiosk` and `Kitchen` from `UserRole` enum. These become `DeviceModeCatalog` entries only.

- `Kiosk` → device mode, no user login needed.
- `Kitchen` → device mode; if a kitchen staff member needs a user account, their role is something like `KitchenStaff` (a human role).

**Step 3.2 — Final `UserRole` enum**

```csharp
public enum UserRole
{
    Owner,
    Manager,
    Cashier,
    Waiter,
    KitchenStaff,
    Host
}
```

**Step 3.3 — Migrate existing data**

- Users with `Role = "Kitchen"` → update to `KitchenStaff`.
- Users with `Role = "Kiosk"` → evaluate case-by-case; likely delete (phantom users).

### Phase 4: Device Management Endpoints

**Step 4.1 — CRUD for Devices (Owner/Manager)**

| Method | Route | Description |
|--------|-------|-------------|
| `GET` | `/api/devices` | List devices for branch |
| `GET` | `/api/devices/{id}` | Device detail |
| `PATCH` | `/api/devices/{id}/mode` | Change device mode (re-provision) |
| `PATCH` | `/api/devices/{id}/toggle` | Enable/disable device |
| `DELETE` | `/api/devices/{id}` | Decommission device |

**Step 4.2 — Link CashRegister to Device**

When a `Device` with `mode=cashier` is activated:
- Auto-create or link a `CashRegister` record.
- Set `Device.CashRegisterId`.
- Remove `CashRegister.DeviceUuid` (redundant — the link is via `Device`).

**Step 4.3 — Device Heartbeat**

```
POST /api/devices/heartbeat   [Authorize]
```

- Read `deviceId` from JWT claim.
- Update `Device.LastSeenAt`.
- Return any pending commands (future: remote config push).

### Phase 5: Endpoint Authorization Hardening

**Step 5.1 — Audit all controllers**

Apply `[RequiresDeviceMode(...)]` where the endpoint is device-specific:

| Endpoint Group | Required Mode |
|----------------|--------------|
| Kitchen status updates | `kitchen` |
| Kiosk order placement | `kiosk` |
| Cash register operations | `cashier` |
| Table management | `tables` |

**Step 5.2 — Dual authorization**

Some endpoints need both role AND device mode:

```csharp
[Authorize(Roles = "Cashier,Manager")]
[RequiresDeviceMode("cashier")]
public async Task<IActionResult> OpenCashSession(...)
```

This ensures: only a Cashier/Manager human, on a POS device, can open a cash session.

---

## 4. Summary Matrix

| Gap | Phase | Effort | Priority |
|-----|-------|--------|----------|
| GAP-1: No Device entity | Phase 1 | Medium | **Critical** |
| GAP-2: Mode is fire-and-forget | Phase 1 | Low | **Critical** |
| GAP-3: JWT has no device context | Phase 2 | Medium | **High** |
| GAP-4: Role enum conflates roles/modes | Phase 3 | Medium | **High** |
| GAP-5: No device-scoped token | Phase 2 | Medium | **High** |
| GAP-6: CashRegister ≠ Device | Phase 4 | Low | **Medium** |

---

## 5. Migration Risk Notes

- **Phase 3 (Role cleanup)** requires a data migration for existing users with `Kitchen`/`Kiosk` roles. Run in a transaction with rollback plan.
- **Phase 4.2 (CashRegister link)** changes a column that has a unique index (`BranchId, DeviceUuid`). Plan the migration carefully — remove the old index before dropping the column.
- **Phase 2.1 (Device login)** introduces a new auth flow. The existing `pin-login` must continue working during transition. Deprecate it only after all frontends adopt `device-login`.
