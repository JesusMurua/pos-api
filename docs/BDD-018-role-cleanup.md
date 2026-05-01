# BDD-018 — Role Cleanup: Purge Hardware Identities from `UserRole`
**Fase:** 21 | **Estado:** Implemented | **Fecha:** 2026-05-01
**Documentos relacionados:**
- [AUDIT-011-backend-roles-devices.md](AUDIT-011-backend-roles-devices.md) — Original gap analysis (2026-04-07) that called out the role/device-mode conflation as GAP-4 and proposed Phase 3 cleanup.
- [BDD-014-device-security-and-management.md](BDD-014-device-security-and-management.md) — Established `type=device` JWT claims and the `DeviceActiveAuthorizationFilter`, the device-auth path that supersedes Kitchen/Kiosk as human roles.

---

## 1. Executive Summary

### 1.1 Problem Statement

A live 403 on `GET /api/CashRegister/session` traced back to a cross-stack ID drift:

| `RoleId` | Frontend | Backend `UserRole` enum | DB `UserRoleCatalog` |
|:---:|---|---|---|
| 3 | Manager | Cashier | Cashier |
| 4 | Cashier | Kitchen | Kitchen |
| 5 | Waiter | Waiter | Waiter |
| 6 | Kitchen | Kiosk | Kiosk |
| 8 | Kiosk | *(none)* | *(none)* |

Frontend was sending `RoleId=4` (its `Cashier`); the backend interpreted that as `Kitchen`, signed the JWT with role `"Kitchen"`, and the cashier endpoints (`[Authorize(Roles="Owner,Manager,Cashier")]`) rejected it with 403.

The root cause was deeper than a numeric mismatch: AUDIT-011 §GAP-4 had already flagged that the backend `UserRole` enum conflated **human roles** (`Owner`, `Manager`, `Cashier`, `Waiter`, `Host`) with **hardware modes** (`Kitchen` for KDS, `Kiosk` for self-service). Mixing them invited exactly this kind of drift — every time the frontend or the backend renumbered the enum, the other side fell out of sync.

### 1.2 Proposed Solution

Adopt **Option B** from the role-cleanup audit:

1. **Remove** `Kitchen` and `Kiosk` from the human `UserRole` enum entirely. These identities are expressed exclusively through `DeviceModeCodes` + Device-JWT auth ([BDD-014](BDD-014-device-security-and-management.md)), never through human PIN-login.
2. **Renumber** the surviving enum members to a compact 1..5 sequence — `Owner=1, Manager=2, Cashier=3, Waiter=4, Host=5`. Cashier stays at 3 (matches the canonical backend numbering); Waiter moves 5→4 and Host moves 7→5 to close the gaps.
3. **Synchronize** four sources of truth in lock-step: the enum, the `UserRoleIds` constants, the `DbInitializer` runtime seed, and the `UserRoleCatalog` table (via EF migration with embedded SQL).
4. **Strip** every `[Authorize(Roles = "...,Kitchen,...")]` attribute — Kitchen humans no longer exist as a role, so listing it serves no purpose. KDS terminals reach kitchen-display endpoints through Device-JWT + `DeviceActiveAuthorizationFilter`, not via role-based MVC authorization.

### 1.3 Expected Outcome / Impact

- **Single canonical role contract.** Frontend, backend, and DB agree on `Cashier=3` (and the 4 other roles). The drift class that produced the 403 cannot recur for these IDs.
- **Cleaner conceptual model.** Humans → `UserRole`; hardware → `DeviceModeCodes`. No overlap.
- **Reduced authorization surface.** 23 endpoints across 10 controllers shrink their role lists by removing a token that no human will ever own again.
- **Frontend follow-up required (out of scope here).** The Angular `UserRole` enum still uses the legacy `Cashier=4` numbering; once the frontend aligns to `Cashier=3` (and removes its `Admin`, `Kitchen`, `Kiosk` entries), the original 403 disappears at the source.

---

## 2. Refactor Inventory

### 2.1 Code Changes

| Layer | File | Change |
|---|---|---|
| Domain | [POS.Domain/Enums/UserRole.cs](../POS.Domain/Enums/UserRole.cs) | Removed `Kitchen=4` and `Kiosk=6`; renumbered to `Owner=1, Manager=2, Cashier=3, Waiter=4, Host=5`; XML doc tightened to forbid re-introducing hardware modes. |
| Domain | [POS.Domain/Helpers/UserRoleIds.cs](../POS.Domain/Helpers/UserRoleIds.cs) | Removed `Kitchen` / `Kiosk` constants and corresponding `ToCode` switch arms; default `_` arm still falls back to `"Cashier"`. |
| Repository | [POS.Repository/DbInitializer.cs](../POS.Repository/DbInitializer.cs) | `UserRoleCatalog` seed reduced from 7 rows to 5; `Level` aligned to new IDs (Waiter=4, Host=5). |
| Repository | [POS.Repository/ApplicationDbContext.cs](../POS.Repository/ApplicationDbContext.cs) | Removed `HasData` seed for User Id=3 ("Cocina", `RoleId=Kitchen`) and the matching UserBranch (UserId=3, BranchId=1). |
| Services | [POS.Services/Service/UserService.cs](../POS.Services/Service/UserService.cs) | `PinRoleIds` no longer includes `Kitchen` or `Kiosk`. New value: `{ Cashier, Waiter, Host }`. |
| API | 10 controllers (BranchController, CategoriesController, DeliveryController, InventoryController, OrdersController, PrintJobController, ProductsController, PushController, SubscriptionController, TableController) | 23 `[Authorize(Roles="...,Kitchen,...")]` attributes lost the `Kitchen` token. No `[Authorize(Roles="...,Kiosk,...")]` attributes existed in the codebase. |

### 2.2 Migration

**File:** [POS.Repository/Migrations/20260501072505_RemoveHardwareRolesAndRenumber.cs](../POS.Repository/Migrations/20260501072505_RemoveHardwareRolesAndRenumber.cs)

The migration carries two concerns interleaved:

| Origin | Operation |
|---|---|
| **Manual SQL** (top of `Up()`) | (1) Wipe `Users` with `RoleId IN (4, 6)`. (2) Wipe `UserRoleCatalogs` rows for Kitchen/Kiosk. (3) Drop `FK_Users_UserRoleCatalogs_RoleId`. (4) Renumber catalog Waiter `5→4` and Host `7→5` (also fixing `Level`). (5) Realign `Users.RoleId` to the new catalog IDs. (6) Re-create the FK with `ON DELETE NO ACTION` (its original semantics). |
| **EF auto-generated** | `DeleteData` for the `User` Id=3 seed and its companion `UserBranch` (1, 3). These are no-ops at apply time because the manual SQL already removed both rows (the User via the explicit `DELETE`, the UserBranch via `CASCADE` on `FK_UserBranches_Users_UserId`). They remain in the migration body so the EF model snapshot stays internally consistent. |

### 2.3 Why the FK Drop / Recreate Was Mandatory

PostgreSQL's `FK_Users_UserRoleCatalogs_RoleId` was created with the EF default `ON UPDATE NO ACTION`. Renumbering a parent PK that has dependent rows fails under that policy from either direction:

| Order attempted | Failure |
|---|---|
| Update parent first (`UPDATE UserRoleCatalogs SET Id = 4 WHERE Code = 'Waiter'`) | Aborts: `Users` rows still reference the old `Id = 5` |
| Update children first (`UPDATE Users SET RoleId = 4 WHERE RoleId = 5`) | Aborts: catalog has no `Id = 4` after the Kitchen delete |

The constraint must be temporarily lifted to allow the intermediate state. Re-creating it with the same `ON DELETE NO ACTION` semantics keeps the post-migration FK behavior identical to the pre-migration one — no semantic shift, only a transactional break.

---

## 3. Acceptance Criteria

| # | Criterion | Verification |
|---|---|---|
| AC-1 | `UserRole` enum has exactly 5 members in 1..5 order | `cat POS.Domain/Enums/UserRole.cs` |
| AC-2 | No production code references `UserRole.Kitchen`, `UserRole.Kiosk`, `UserRoleIds.Kitchen`, or `UserRoleIds.Kiosk` | `grep -r "UserRole(Ids)?\.K(itchen|iosk)" POS.*` returns 0 hits |
| AC-3 | DB `UserRoleCatalogs` has exactly 5 rows (1=Owner, 2=Manager, 3=Cashier, 4=Waiter, 5=Host) | `SELECT * FROM "UserRoleCatalogs" ORDER BY "Id";` matches the table in §4 |
| AC-4 | DB `Users` has no row with `RoleId IN (4, 6)` (legacy Kitchen/Kiosk values) | `SELECT COUNT(*) FROM "Users" WHERE "RoleId" IN (6, 7);` returns 0 (note: 4 is now Waiter) |
| AC-5 | `FK_Users_UserRoleCatalogs_RoleId` exists with `ON DELETE NO ACTION` | `pg_constraint` query returns `confdeltype = 'r'` (Restrict) |
| AC-6 | `dotnet build` returns 0 errors | Verified |
| AC-7 | No `[Authorize(Roles=...)]` attribute in `POS.API/Controllers/` mentions `Kitchen` or `Kiosk` | `grep -r "Kitchen\|Kiosk" POS.API/Controllers/*.cs` returns 0 hits in `Authorize` lines |

## 4. Post-Migration DB State

```
 Id |  Code   |  Name   | Level
----+---------+---------+-------
  1 | Owner   | Dueño   |     1
  2 | Manager | Gerente |     2
  3 | Cashier | Cajero  |     3
  4 | Waiter  | Mesero  |     4
  5 | Host    | Hostess |     5
```

`Users` table after migration: User Id=3 (Cocina) deleted; remaining users reassigned where applicable (no Users had `RoleId = 7` in this dev DB, and the only Waiter had been seeded as User 2 / Cashier — so no `RoleId 5→4` propagation was needed in practice).

---

## 5. Out of Scope (Frontend Follow-Up)

The Angular `UserRole` enum still ships:

```ts
Owner = 1, Admin = 2, Manager = 3, Cashier = 4, Waiter = 5, Kitchen = 6, Host = 7, Kiosk = 8
```

This enum must be aligned to the new backend canonical sequence (`Owner=1, Manager=2, Cashier=3, Waiter=4, Host=5`) — including the rename `Admin → Manager`. **Until the frontend update ships, any cashier user will continue to PIN-login successfully but cannot consume the role-gated endpoints**, because the wrong `RoleId` is sent during login and the JWT carries the wrong role string. Tracking work for a separate ticket / PR.

---

## 6. Architectural Lesson Captured

> The `UserRole` enum's XML doc has carried *"Do NOT renumber these values without a coordinated DB migration"* since BDD-012, but did not warn against **adding** values that would later need to be removed. AUDIT-011 §GAP-4 spotted that mixing human and hardware identities was a latent bug; this BDD finally pays the deferred cost of that decision.
>
> **Heuristic for future role/permission additions:** any new identity proposed for `UserRole` must be a human role with a PIN-login flow. Hardware modes (KDS, Kiosk, KDS-Bar, anything authenticated via Device-JWT) belong in `DeviceModeCodes` exclusively. The next time someone tries to extend `UserRole` with a hardware concept, this BDD is the rejection precedent.

---

**End of BDD-018.**
