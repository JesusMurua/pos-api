# BDD-013 — Branch TimeZone & UTC Reporting Boundaries
**Fase:** 18 | **Estado:** Proposed | **Fecha:** 2026-04-19
**Documentos relacionados:** [AUDIT-016-backend-schema-reality.md](AUDIT-016-backend-schema-reality.md) — §2 "Fields That Do NOT Exist" identifica `Timezone` como campo ausente en el dominio.

---

## 1. Executive Summary

### 1.1 Problem Statement

The `Branch` entity has no notion of its own timezone. All "per-day" queries in `OrderRepository` (KPI summary, reports, dashboard) rely on two equally broken patterns: `o.CreatedAt.Date == date.Date` and inclusive-date ranges like `o.CreatedAt < toDate.AddDays(1)`. EF Core translates these expressions to PostgreSQL constructs that (a) coerce `timestamptz` columns against `timestamp` literals whose `DateTime.Kind` is `Unspecified`, producing `Npgsql` runtime errors that surface as HTTP 500, and (b) destroy the `IX_Orders_BranchId_CreatedAt` index because the comparison is against a computed date function instead of a raw column range. Additionally, the timezone used for "today" is read from the `X-Timezone` HTTP header per request, meaning two devices making reports against the same branch can see different day boundaries.

### 1.2 Proposed Solution

Promote timezone to a first-class persistent field on `Branch` (`TimeZoneId`, IANA string, default `"America/Mexico_City"`). Introduce a single UTC-range helper `TimeZoneHelper.GetUtcRangeForLocalDate(DateOnly, string)` that returns the exact `[startUtc, endUtc)` half-open window for a local calendar day, handling DST transitions. Refactor `OrderRepository.GetByBranchAndDateAsync` (and every sibling method that slices by day) to accept `DateTime startUtc, DateTime endUtc` already pinned to `DateTimeKind.Utc`, and query with `o.CreatedAt >= startUtc && o.CreatedAt < endUtc`. The service layer becomes the single orchestrator: it loads `Branch.TimeZoneId`, computes the UTC range via the helper, and hands pure UTC `DateTime`s to the repository.

### 1.3 Expected Outcome / Impact

- The Npgsql `Kind=Unspecified` 500 class of errors in daily/summary endpoints is eliminated by construction — repositories only see `Kind=Utc` inputs.
- The composite index `(BranchId, CreatedAt)` is used as a range scan instead of a sequential filter, improving P95 latency of daily queries on multi-million-row branches.
- Every branch has a deterministic, auditable day boundary; two devices querying the same branch agree on what "today" means regardless of each client's local clock.
- Registration captures the matrix branch's timezone at creation time, laying the groundwork for multi-country expansion without further migrations.

---

## 2. Current State Analysis

### 2.1 `Branch` Entity Today

[POS.Domain/Models/Branch.cs](../POS.Domain/Models/Branch.cs) has no timezone field. The persistent properties are `Id, BusinessId, Name, LocationName, PinHash, IsMatrix, FolioPrefix, FolioCounter, FolioFormat, HasKitchen, HasTables, HasDelivery, IsActive, FiscalZipCode, CreatedAt` plus navigations. [AUDIT-016 §2.1](AUDIT-016-backend-schema-reality.md) confirms `Timezone` is explicitly listed under "Fields That Do NOT Exist".

### 2.2 Current Timezone Resolution

- Frontend sends `X-Timezone: <IANA>` HTTP header.
- [POS.API/Extensions/HttpContextExtensions.cs](../POS.API/Extensions/HttpContextExtensions.cs) exposes `GetClientTimeZone()` returning `string?`.
- Controllers pass the header value downstream; [TableController.cs:50](../POS.API/Controllers/TableController.cs#L50) is the current exemplar.
- [POS.Domain/Helpers/TimeZoneHelper.cs](../POS.Domain/Helpers/TimeZoneHelper.cs) exposes exactly two members: `DefaultTimeZone = "America/Mexico_City"`, `GetLocalToday(string?) → DateOnly`, `GetTimeZoneInfo(string?) → TimeZoneInfo`. No UTC-range helper exists.

### 2.3 Broken Query Patterns in `OrderRepository`

Surveyed call sites in [POS.Repository/Repository/OrderRepository.cs](../POS.Repository/Repository/OrderRepository.cs):

| Line(s) | Method / Scope | Pattern |
|---|---|---|
| 18 | `GetByBranchAndDateAsync` | `o.CreatedAt.Date == date.Date` |
| 36 | `GetDailySummaryAsync` | `o.CreatedAt.Date == date.Date` |
| 84-85 | `GetDailyMetricsAsync` | `o.CreatedAt.Date >= from.Date && o.CreatedAt.Date <= to.Date` |
| 104-105 | Payment-method totals | Same `.Date >= / <=` pattern |
| 122-123 | Top products | Same `.Date >= / <=` pattern |
| 143-144 | Order report rows | Same `.Date >= / <=` pattern |
| 177-178 | Fiscal CSV rows | Same `.Date >= / <=` pattern |
| 212-213, 288-289, 314-315, 348-349 | BI queries | Range pattern `o.CreatedAt >= fromDate && o.CreatedAt < toDate.AddDays(1)` **with `Kind=Unspecified`** |
| 385, 404 | Cancellation / dashboard | `o.CreatedAt.Date == date.Date` |

Two failure modes coexist:

1. **`.Date == date.Date` and `.Date >= from.Date`** — EF translates to a per-row `date_trunc(...)` / `date(...)` expression, so PostgreSQL cannot use the `(BranchId, CreatedAt)` index as a range scan. On multi-million-row tables this becomes a sequential filter.
2. **`fromDate.AddDays(1)` range queries** — the inputs are `DateTime` values whose `Kind` is `Unspecified` (they come straight from query-string parsing). `Orders.CreatedAt` is `timestamptz` (Npgsql maps `DateTime` with `Kind=Utc` to this). Comparing the column to an `Unspecified` literal triggers `Npgsql` to throw at query execution, which surfaces as HTTP 500 from endpoints like `/api/reports/*`.

### 2.4 Service + Controller Callers

| Caller | File | Current contract |
|---|---|---|
| `OrderService.GetByBranchAndDateAsync(int, DateTime)` | [POS.Services/Service/OrderService.cs:619](../POS.Services/Service/OrderService.cs#L619) | Pass-through to repo |
| `OrderService.GetDailySummaryAsync(int, DateTime)` | [POS.Services/Service/OrderService.cs:627](../POS.Services/Service/OrderService.cs#L627) | Pass-through to repo |
| `OrdersController.GetByBranchAndDate` | [POS.API/Controllers/OrdersController.cs:57](../POS.API/Controllers/OrdersController.cs#L57) | `[FromQuery] DateTime date` |
| `OrdersController.GetDailySummary` | [POS.API/Controllers/OrdersController.cs:72](../POS.API/Controllers/OrdersController.cs#L72) | `[FromQuery] DateTime date` |

No caller converts the date to UTC; the `DateTime` flows end-to-end with `Kind=Unspecified`.

### 2.5 Registration Flow Today

[AuthService.RegisterAsync](../POS.Services/Service/AuthService.cs) creates the matrix branch inline. [POS.API/Models/AuthRequests.cs:39-75](../POS.API/Models/AuthRequests.cs#L39-L75) (`RegisterApiRequest`) does **not** accept a timezone field. `RegisterRequest` on the service side (same file, lower) likewise has no timezone. The matrix branch is created with no timezone.

---

## 3. Technical Requirements

### 3.1 Functional Requirements

| ID | Requirement | Acceptance Criteria |
|---|---|---|
| **FR-001** | `Branch` entity owns a persistent `TimeZoneId` | New property of type `string`, `[MaxLength(50)]`, **non-nullable** at the entity level with default `"America/Mexico_City"`. The column is `NOT NULL` in PostgreSQL. |
| **FR-002** | EF Core migration backfills existing rows | The migration sets every existing `Branches.TimeZoneId` to `"America/Mexico_City"` before applying the `NOT NULL` constraint. New inserts that omit the value also default to `"America/Mexico_City"` at the column-default level so raw SQL inserts remain safe. |
| **FR-003** | `TimeZoneHelper.GetUtcRangeForLocalDate` is the sole source of UTC-range math | New static method with signature `(DateTime startUtc, DateTime endUtc) GetUtcRangeForLocalDate(DateOnly localDate, string ianaTimeZone)`. Both outputs have `DateTimeKind.Utc`. Range is half-open: `[startUtc, endUtc)`. DST transitions handled (see §6.1). |
| **FR-004** | Registration accepts and persists `TimeZoneId` | `RegisterApiRequest` and `RegisterRequest` DTOs both expose `TimeZoneId: string?`. When null/empty/unknown, falls back to `"America/Mexico_City"`. `AuthService.RegisterAsync` persists the resolved value on the matrix branch. |
| **FR-005** | `OrderRepository.GetByBranchAndDateAsync` refactored to UTC-range | Repository method signature becomes `GetByBranchAndDateAsync(int branchId, DateTime startUtc, DateTime endUtc)`. Body uses `o.CreatedAt >= startUtc && o.CreatedAt < endUtc`. No `.Date` accessor. No `AddDays(1)` on inputs. |
| **FR-006** | `OrderService` orchestrates the timezone → range conversion | Service loads `Branch.TimeZoneId`, calls the helper, passes only `Utc` `DateTime`s to the repository. The controller boundary accepts `DateOnly` (preferred) from the query string and never does timezone arithmetic. |
| **FR-007** | Sibling queries aligned | Every other method in `OrderRepository` that uses `.Date == / >= / <=` (see §2.3 table) must be migrated to the same range pattern. The migration is in scope to prevent half-fixed state. |

### 3.2 Non-Functional Requirements

- **Performance:**
  - `GET /api/orders?date=YYYY-MM-DD` P95 < 80 ms on a branch with 1 M orders (down from current multi-second on cold cache).
  - Query plan must show index range scan on `IX_Orders_BranchId_CreatedAt`, not a sequential scan or function-based filter.
- **Correctness:**
  - All `DateTime` values crossing the repository boundary have `DateTimeKind.Utc` — this is an invariant, not a best-effort.
  - The half-open range `[startUtc, endUtc)` must not double-count orders at midnight boundaries nor miss orders at DST "spring-forward" gaps.
- **Backward compatibility:**
  - `X-Timezone` header remains in place for unauthenticated flows and UI preferences; `Branch.TimeZoneId` is the **authoritative** source for any server-side per-day arithmetic on branch-scoped data.
  - Existing JWTs remain valid — no claim changes.
- **Data integrity:** The migration is non-destructive; no existing data is altered beyond the `TimeZoneId` backfill.

---

## 4. Architecture Design

### 4.1 Component Overview

| Component | Change |
|---|---|
| `POS.Domain/Models/Branch.cs` | Add `TimeZoneId` property |
| `POS.Domain/Helpers/TimeZoneHelper.cs` | Add `GetUtcRangeForLocalDate` method |
| EF Core migration | Add `TimeZoneId` column, backfill existing rows, apply NOT NULL |
| `POS.Repository/ApplicationDbContext.cs` | Configure column default at the builder level (defense in depth) |
| `POS.API/Models/AuthRequests.cs` | Add `TimeZoneId` to `RegisterApiRequest` |
| `POS.Services/IService/IAuthService.cs` | Add `TimeZoneId` to `RegisterRequest` |
| `POS.Services/Service/AuthService.cs` | Persist `TimeZoneId` on the matrix branch |
| `POS.Repository/IRepository/IOrderRepository.cs` | Signature change: replace `DateTime date` with `DateTime startUtc, DateTime endUtc` on every day-slicing method |
| `POS.Repository/Repository/OrderRepository.cs` | Rewrite every `.Date`-based query to range pattern |
| `POS.Services/Service/OrderService.cs` | Orchestrate branch → timezone → UTC range → repo call |
| `POS.API/Controllers/OrdersController.cs` | Accept `DateOnly` from query string; no timezone handling at this layer |
| No new repositories | `Branch` already has `GetByIdAsync` via existing `IBranchRepository` |

### 4.2 Data Flow — `GET /api/orders?date=YYYY-MM-DD`

1. Controller receives the request; binds the query string to `DateOnly date`.
2. Controller reads `BranchId` from the JWT claim (via `BaseApiController.BranchId`).
3. Controller calls `_orderService.GetByBranchAndDateAsync(branchId, date)`.
4. Service loads `Branch` by id → reads `TimeZoneId`. If the row is missing, throws `NotFoundException` (defensive; should never happen under normal auth).
5. Service calls `TimeZoneHelper.GetUtcRangeForLocalDate(date, branch.TimeZoneId)` → `(startUtc, endUtc)`.
6. Service calls `_unitOfWork.Orders.GetByBranchAndDateAsync(branchId, startUtc, endUtc)`.
7. Repository issues `WHERE BranchId = @id AND CreatedAt >= @start AND CreatedAt < @end ORDER BY CreatedAt DESC` — indexable range scan.
8. Result streams back unchanged.

### 4.3 Data Flow — Registration with TimeZone

1. Frontend `POST /api/auth/register` with optional `timeZoneId: "America/Mexico_City"` (or any IANA id).
2. Controller maps to `RegisterRequest.TimeZoneId`.
3. `AuthService.RegisterAsync` resolves the incoming value:
   - If null/empty → `"America/Mexico_City"`.
   - If the string does not resolve via `TimeZoneInfo.FindSystemTimeZoneById` → validation error (see VR-001).
   - Otherwise → persist as-is.
4. Matrix `Branch` is created with `TimeZoneId = resolved`.

### 4.4 Database Schema Changes

**Modified table:** `Branches`
- **New column:** `TimeZoneId` — `TEXT` (PostgreSQL maps `string [MaxLength(50)]` to `character varying(50)`), `NOT NULL`, column default `'America/Mexico_City'`.
- **Backfill step:** set `TimeZoneId = 'America/Mexico_City'` on every existing row before applying the NOT NULL constraint.
- **Index:** **None** required. The column is not selective enough to warrant a new index. Existing `IX_Orders_BranchId_CreatedAt` is what the refactored queries rely on; no change needed there.
- **No new tables.**
- **No changes to `Orders` schema.** The repository refactor is query-level only.

### 4.5 Migration Requirements

The EF Core migration `AddTimeZoneIdToBranch` must:

1. `AddColumn<string>("TimeZoneId", "Branches", maxLength: 50, nullable: true)` — nullable during backfill.
2. Execute a raw SQL `UPDATE Branches SET "TimeZoneId" = 'America/Mexico_City' WHERE "TimeZoneId" IS NULL` to backfill.
3. `AlterColumn<string>("TimeZoneId", "Branches", maxLength: 50, nullable: false, defaultValue: "America/Mexico_City")` to enforce NOT NULL and install the column default.
4. Update the model snapshot accordingly.

`Down()` must drop the column without data loss elsewhere.

---

## 5. API Contract

### 5.1 Modified Endpoints

| Endpoint | Change |
|---|---|
| `POST /api/auth/register` | Request body gains optional `timeZoneId: string`. Response shape unchanged (still `AuthResponse`). |
| `GET /api/orders?date=YYYY-MM-DD` | Query-string type preference: `DateOnly`. Behavior change: day boundaries now computed against `Branch.TimeZoneId` instead of client header. |
| `GET /api/orders/summary?date=YYYY-MM-DD` | Same change as above. |

No new routes. No endpoints removed.

### 5.2 Modified DTO — `RegisterApiRequest`

Additions in [POS.API/Models/AuthRequests.cs](../POS.API/Models/AuthRequests.cs):

| Field | Type | Required | Validation |
|---|---|---|---|
| `TimeZoneId` | `string?` | No | `[MaxLength(50)]`. Must resolve via `TimeZoneInfo.FindSystemTimeZoneById` if provided (service-layer check). |

Mirrors into service-layer `RegisterRequest`.

### 5.3 Modified Service Signatures

| Before | After |
|---|---|
| `Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateTime date)` (repo) | `Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateTime startUtc, DateTime endUtc)` |
| `Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateTime date)` (repo) | `Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateTime startUtc, DateTime endUtc)` |
| `Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateTime date)` (service) | `Task<IEnumerable<Order>> GetByBranchAndDateAsync(int branchId, DateOnly localDate)` |
| `Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateTime date)` (service) | `Task<IEnumerable<Order>> GetDailySummaryAsync(int branchId, DateOnly localDate)` |

The service layer is the type-conversion boundary: it takes a calendar date, turns it into a UTC range. Controllers **never** construct a `DateTime` for date filtering.

### 5.4 New Helper Contract — `TimeZoneHelper.GetUtcRangeForLocalDate`

```
public static (DateTime startUtc, DateTime endUtc) GetUtcRangeForLocalDate(
    DateOnly localDate,
    string ianaTimeZone)
```

**Behavior:**
- Resolves `ianaTimeZone` via the existing `GetTimeZoneInfo` (silent fallback to `"America/Mexico_City"`).
- Builds a local `DateTime` at `00:00:00` of `localDate` with `DateTimeKind.Unspecified`.
- Converts to UTC via `TimeZoneInfo.ConvertTimeToUtc(localMidnight, tz)` — this call is DST-aware (see §6.1).
- `endUtc` is computed by adding exactly 24 hours to `localDate + 1` (next-day local midnight) converted to UTC. On DST-transition days, this may produce a 23- or 25-hour span; that is **correct** behavior because it matches the wall-clock day length.
- Returned values are always `DateTimeKind.Utc`.

### 5.5 HTTP Status Codes

All affected endpoints keep their current status-code contract. New codes:

| Endpoint | Scenario | Code |
|---|---|---|
| `POST /api/auth/register` | `TimeZoneId` provided but not a valid IANA id | `400 Bad Request` + `ValidationException` |

---

## 6. Business Logic Specifications

### 6.1 `GetUtcRangeForLocalDate` Algorithm

```
function GetUtcRangeForLocalDate(localDate: DateOnly, ianaTimeZone: string)
        : (startUtc: DateTime, endUtc: DateTime)
    tz := GetTimeZoneInfo(ianaTimeZone)            // silent fallback on failure
    localStart := new DateTime(localDate.Year, localDate.Month, localDate.Day,
                               0, 0, 0, DateTimeKind.Unspecified)
    localEnd   := localStart.AddDays(1)            // next-day local midnight
    startUtc := TimeZoneInfo.ConvertTimeToUtc(localStart, tz)
    endUtc   := TimeZoneInfo.ConvertTimeToUtc(localEnd,   tz)
    return (startUtc, endUtc)
```

**DST edge-case table:**

| Scenario | localStart | localEnd | Expected span |
|---|---|---|---|
| Normal day | 2026-06-15 00:00 | 2026-06-16 00:00 | 24h |
| Spring-forward (northern hemisphere, March) | 2026-03-08 00:00 (PT) | 2026-03-09 00:00 (PT) | 23h |
| Fall-back (northern hemisphere, November) | 2026-11-01 00:00 (PT) | 2026-11-02 00:00 (PT) | 25h |
| Invalid local time (e.g., 02:30 on spring-forward day) | Not applicable — we use 00:00 | — | — |
| Ambiguous local time (fall-back 01:30 occurs twice) | Not applicable — we use 00:00 | — | — |

Because the helper anchors at local midnight, it **never** hits invalid or ambiguous local times on typical DST jurisdictions. Regions with midnight DST transitions (rare — some historical zones) are covered by `TimeZoneInfo.ConvertTimeToUtc` using its standard-time resolution policy.

### 6.2 Validation Rules

| ID | Rule | Error |
|---|---|---|
| **VR-001** | `TimeZoneId` on registration must be resolvable via `TimeZoneInfo.FindSystemTimeZoneById` when non-empty | `ValidationException("TimeZoneId is not a valid IANA timezone identifier")` → HTTP 400 |
| **VR-002** | Repository methods reject `DateTime` inputs whose `Kind != Utc` | Defensive guard that throws `ArgumentException("startUtc must have DateTimeKind.Utc")` to catch regressions early |
| **VR-003** | Service-layer conversion never emits `startUtc == endUtc` | Assert in helper: `endUtc > startUtc`; otherwise `InvalidOperationException` |

### 6.3 Edge Cases

| Case | Behavior |
|---|---|
| Client sends `date=2026-03-08` to a Pacific-Time branch on a DST day | Returns orders in a 23-hour window (local wall-clock day); no missing rows |
| Client sends `date=2026-11-01` to a Pacific-Time branch (fall-back) | Returns orders in a 25-hour window; no duplicates because the range is half-open |
| `Branch.TimeZoneId` contains an unknown string (e.g., operator typo at registration) | Helper falls back to `"America/Mexico_City"` silently (existing contract). No 500. The VR-001 check prevents this at registration time, but older rows predating the migration could still host garbage; the fallback is the safety net. |
| Repository is called with `Kind=Unspecified` by a future caller who bypassed the service | Defensive guard throws `ArgumentException` (see VR-002) — regression is caught in tests, not production |
| `date` query-string value is malformed (e.g., `2026-13-40`) | Model binding fails → `400` with default response (existing behavior preserved) |
| Branch has no `TimeZoneId` (row predates migration and backfill failed) | Impossible by construction after migration; defensive fallback in service is `"America/Mexico_City"` |
| Sibling repository methods still on the old pattern (e.g., BI reports) | Explicit in §2.3; scope includes migrating them — not a follow-up |

---

## 7. Performance Optimization Strategy

### 7.1 Query Optimization

- **Before:** `WHERE BranchId = @id AND date(CreatedAt) = @date` → sequential scan or function-based index (we have none) on multi-million-row tables.
- **After:** `WHERE BranchId = @id AND CreatedAt >= @start AND CreatedAt < @end` → uses `IX_Orders_BranchId_CreatedAt` as a B-tree range scan.
- **Projection strategy:** unchanged — we return full `Order` entities because callers need item/payment graphs. Eager loading of `Items` and `Payments` remains as-is.
- **No new includes** required for this refactor.

### 7.2 Bulk Operations & Transactions

Not applicable — all affected endpoints are read-only.

### 7.3 Caching Strategy

No new cache. The `TimeZoneInfo` lookups in `TimeZoneHelper` are already fast (OS-level cache). A per-branch memoization of `TimeZoneId` is **not** introduced now; the cost of one `SELECT TimeZoneId FROM Branches WHERE Id=@id` against a PK is negligible compared to the order-fetch that follows. If profiling later proves this is hot, a lightweight `IMemoryCache` layer can be added without changing the contract.

---

## 8. Error Handling Strategy

| Scenario | Exception | HTTP Status | Logging |
|---|---|---|---|
| Invalid `TimeZoneId` at registration | `ValidationException` (existing) | 400 | INFO |
| Unknown `TimeZoneId` in DB (post-migration regression) | Silent fallback in helper; no exception | 200 | WARN with branch id + raw value |
| Repository invoked with non-UTC `DateTime` | `ArgumentException` (new defensive guard) | 500 | ERROR with stack trace |
| Branch not found during service orchestration | `NotFoundException` (existing) | 404 | INFO |
| `TimeZoneInfo.FindSystemTimeZoneById` unavailable on host (missing ICU / tz data) | Helper fallback to default; log once at startup | — | ERROR at app bootstrap |

---

## 9. Testing Requirements

### 9.1 Unit Tests — `TimeZoneHelper.GetUtcRangeForLocalDate`

- Returns 24-hour UTC span for a normal non-DST day in `"America/Mexico_City"`.
- Returns 23-hour UTC span on spring-forward day in `"America/Los_Angeles"`.
- Returns 25-hour UTC span on fall-back day in `"America/Los_Angeles"`.
- Returns 24-hour UTC span for `"UTC"` identifier.
- Both outputs carry `DateTimeKind.Utc`.
- Falls back silently when IANA string is unknown (returns range for `"America/Mexico_City"`).
- `endUtc > startUtc` holds for every jurisdiction tested.

### 9.2 Unit Tests — `AuthService.RegisterAsync`

- Persists provided `TimeZoneId` on the matrix branch.
- Falls back to `"America/Mexico_City"` when `TimeZoneId` is null/empty.
- Throws `ValidationException` when `TimeZoneId` is a non-IANA string.

### 9.3 Unit Tests — `OrderService.GetByBranchAndDateAsync`

- Loads `Branch.TimeZoneId` before querying orders.
- Passes `Kind=Utc` bounds to the repository.
- Propagates `NotFoundException` when the branch does not exist.

### 9.4 Repository Contract Tests

- Calling `GetByBranchAndDateAsync` with `Kind=Unspecified` throws `ArgumentException`.
- Generated SQL uses `>=` and `<` (verified with `ToQueryString()`), not `date(...)` nor `date_trunc(...)`.

### 9.5 Integration Tests

- `GET /api/orders?date=2026-04-19` against a branch with `TimeZoneId = "America/Mexico_City"` returns exactly the orders whose `CreatedAt` falls within the local calendar day.
- Same call against a branch with `TimeZoneId = "America/Los_Angeles"` on a DST-transition day returns the correct 23-/25-hour window.
- `POST /api/auth/register` with `timeZoneId = "America/Mexico_City"` → 200; matrix branch persists the value.
- `POST /api/auth/register` with `timeZoneId = "Mars/Olympus_Mons"` → 400.

### 9.6 Performance Tests

- `GET /api/orders?date=…` P95 < 80 ms with 1 M orders on a single branch. Baseline should be recorded before the migration for comparison.
- `EXPLAIN ANALYZE` shows `Index Scan using IX_Orders_BranchId_CreatedAt`, not `Seq Scan`.

---

## 10. Implementation Phases

| Phase | Scope | Dependencies | Complexity |
|---|---|---|---|
| **Phase 1** | Add `TimeZoneId` to `Branch` entity + EF Core migration (backfill + NOT NULL + column default) + snapshot update | None | **Medium** |
| **Phase 2** | Add `GetUtcRangeForLocalDate` helper + unit tests covering normal / DST / fallback cases | Phase 1 may run in parallel | **Low** |
| **Phase 3** | Update `RegisterApiRequest` / `RegisterRequest` / `AuthService.RegisterAsync` to capture and persist `TimeZoneId` including VR-001 | Phase 1 | **Low** |
| **Phase 4** | Refactor `OrderRepository.GetByBranchAndDateAsync` + `GetDailySummaryAsync` to UTC-range signature; update `IOrderRepository`; add VR-002 guard | Phase 2 | **Medium** |
| **Phase 5** | Update `OrderService` callers to orchestrate `Branch.TimeZoneId` → range → repo; update `OrdersController` to bind `DateOnly` | Phase 4 | **Low** |
| **Phase 6** | Migrate remaining `.Date`-based queries in `OrderRepository` (BI, cancellation, dashboard, fiscal, top products — full list in §2.3) | Phase 5 | **Medium** |

Phases 1 and 2 are independent and may ship in parallel. Phase 3 depends on Phase 1. Phases 4 → 5 → 6 are strictly sequential.

---

## 11. Out of Scope

- No `Business`-level timezone (timezone is always branch-local; multi-branch businesses may span zones).
- No changes to the `X-Timezone` header or front-end timezone signaling — UI preference logic is separate.
- No reflection-based auto-discovery of timezones from postal codes.
- No timezone override at the user level.
- No historical re-computation of past reports (the change affects new queries; existing cached dashboards are untouched).
- No new indexes on `Orders`. The existing `(BranchId, CreatedAt)` index is what the refactor relies on.

---

## 12. Security Considerations

- `TimeZoneId` is not sensitive data; no new PII surface.
- Validation (VR-001) prevents operators from persisting arbitrary strings that could break queries later.
- The defensive `ArgumentException` guard (VR-002) prevents a future caller from accidentally re-introducing `Kind=Unspecified` values into the repository — this is a correctness boundary, not a security one, but it does mitigate a class of 500 errors reachable by crafted query strings today.
- No new JWT claim, no new auth path, no new rate-limiting policy.

---

## 13. Risk & Migration Notes

- **Existing 500s will disappear before the full refactor completes.** If Phase 4 ships before Phase 6, only the `/api/orders*` endpoints are fixed; BI and fiscal endpoints still use the old broken pattern. Ship Phase 6 in the same release or accept a documented window of partial coverage.
- **Snapshot drift:** EF Core `HasData` seeds (`ApplicationDbContext.cs:1179`) for the demo matrix branch must include `TimeZoneId` — otherwise the snapshot will diverge from the DB on fresh rebuilds.
- **Test fixtures:** any test that constructs a `Branch` directly must be updated to set `TimeZoneId`; the non-nullable column will break test-DB seeding otherwise.
- **Backup before running migration** — single transactional migration but the `NOT NULL` alter locks the `Branches` table; schedule during a quiet window.
