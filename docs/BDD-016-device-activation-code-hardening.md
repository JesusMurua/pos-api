# BDD-016 — Device Activation Code Hardening
**Fase:** 21 | **Estado:** Proposed | **Fecha:** 2026-04-30
**Documentos relacionados:**
- [BDD-014-device-security-and-management.md](BDD-014-device-security-and-management.md) — Established the per-request `IsActive` gate and the back-office management surface this refactor sits on top of.
- [AUDIT-049-device-activation-register-linking-ux.md](AUDIT-049-device-activation-register-linking-ux.md) — Activation + cash-register linking UX audit; informs the contract assumptions on the existing flow.
- [BDD-017-unified-secure-alphabet.md](BDD-017-unified-secure-alphabet.md) — Follow-up that closed a scope gap discovered post-deploy: `CashRegisterService` had a parallel 32-char generator with `L`/`U` allowed. BDD-017 unified both surfaces under a shared helper and renamed `DeviceActivationAlphabet` → `SecureCodeAlphabet`. References below reflect the post-BDD-017 names.

---

## 1. Executive Summary

### 1.1 Problem Statement

The current activation-code generator in [`DeviceService.GenerateActivationCodeAsync`](../POS.Services/Service/DeviceService.cs) emits a 6-digit numeric code via `_random.Next(100000, 999999).ToString()` against a static `Random` instance ([`DeviceService.cs:17`](../POS.Services/Service/DeviceService.cs#L17)). Three concrete problems compound:

1. **Weak entropy.** 10⁶ ≈ 1 M combinations against an `[AllowAnonymous]` endpoint ([`DeviceController.cs:54`](../POS.API/Controllers/DeviceController.cs#L54)) with no rate limit. A naive script can sweep the keyspace in minutes.
2. **Non-thread-safe RNG.** `System.Random` instantiated as a static field is **not thread-safe** without an external lock; the existing code lacks one. Concurrent code generation on multi-threaded request pipelines is a latent correctness bug.
3. **Lax DTO contract.** [`ActivateDeviceRequest.Code`](../POS.API/Controllers/DeviceController.cs#L141-L143) is gated only by `[StringLength(6, MinimumLength = 6)]` — no charset enforcement, no fail-fast for obvious garbage. Wasted DB round-trips on malformed input. The repository compares raw input verbatim ([`DeviceActivationCodeRepository.cs:18`](../POS.Repository/Repository/DeviceActivationCodeRepository.cs#L18)), so a leading space or a lowercased paste fails silently with the user-visible message *"Invalid activation code"*.

### 1.2 Proposed Solution

Replace the numeric `Random` generator with a CSPRNG-backed helper that pulls 6 characters from a **Crockford-style 30-character alphabet** (the 36-char alphanumeric set minus the visually ambiguous `0, O, 1, I, L, U`). The generator uses **5-bit rejection sampling**: each random byte is masked with `0x1F` (yielding 0-31); bytes whose result is `≥ 30` are discarded and a new byte is drawn. This keeps the output **uniformly distributed across the 30 alphabet positions** with negligible overhead (`~6.25%` of bytes rejected in expectation). Tighten the activation DTO with a `[RegularExpression]` matching the alphabet (case-insensitive via inline `(?i)` flag, since `RegularExpressionAttribute` does not surface `RegexOptions`). Sanitize incoming codes (`Trim().ToUpper()`) at the very top of the service before any DB round-trip. Front the endpoint with a sliding-window rate limiter partitioned by the **real client IP** — recovered through a newly-introduced `ForwardedHeadersMiddleware` that respects Render's reverse proxy. Surface `Retry-After` on every 429 response globally (benefits the two existing fixed-window policies as well). The persistence column was already created as `character varying(6) NOT NULL` in [`20260327204815_AddDeviceActivationCodes`](../POS.Repository/Migrations/20260327204815_AddDeviceActivationCodes.cs); only the C# model annotations need to catch up to that constraint. A dedicated migration (`InvalidateLegacyDeviceActivationCodes`) carries a single `Sql()` statement that marks all in-flight legacy numeric codes as consumed, executed automatically on startup via `db.Database.Migrate()`, so the new regex contract is honored from the first request after deploy.

### 1.3 Expected Outcome / Impact

- **Entropy uplift:** `10⁶ → 30⁶ ≈ 729 M`. Roughly three orders of magnitude harder to enumerate.
- **Brute-force defense in depth:** even with the larger keyspace, an IP-scoped sliding window caps online attempts at 10/min/IP, with `Retry-After` to inform legitimate retries (multi-terminal NAT scenarios remain viable up to 10 concurrent activations per minute).
- **Correctness:** removal of `static Random` eliminates a latent thread-safety bug; CSPRNG is concurrency-safe by contract.
- **UX neutrality:** ambiguous characters (`0/O`, `1/I/L`, `U`) are excluded from the generator, so operators read codes off a screen and type them on a tablet without the typical confusion class.
- **Observability:** rate-limit rejections are logged with the resolved IP; combined with Render's request log, post-launch we can see whether legitimate tenants ever hit 429.
- **Tech debt cleared:** zero backward-compatibility surface (no production tenants), so `[StringLength]` is removed (not deprecated), the `_random` field is deleted (not kept "just in case"), and the column constraint is tightened in the same migration that ships the feature.

---

## 2. Current State Analysis

### 2.1 Code Generation Path

| Aspect | Current | Reference |
|---|---|---|
| RNG | `static readonly Random _random = new()` | [`DeviceService.cs:17`](../POS.Services/Service/DeviceService.cs#L17) |
| Generation expression | `_random.Next(100000, 999999).ToString()` | [`DeviceService.cs:147`](../POS.Services/Service/DeviceService.cs#L147) |
| Charset | digits `0-9` only | implicit |
| Length | 6 (range `[100000, 999998]`, since `Random.Next` upper bound is exclusive) | implicit |
| Collision-safe loop | up to 10 retries against `CodeExistsAsync` | [`DeviceService.cs:144-153`](../POS.Services/Service/DeviceService.cs#L144-L153) |

### 2.2 DTO + Validation Path

| Aspect | Current | Reference |
|---|---|---|
| DTO | `ActivateDeviceRequest { Code, DeviceUuid }` | [`DeviceController.cs:139-154`](../POS.API/Controllers/DeviceController.cs#L139-L154) |
| `Code` annotations | `[Required]`, `[StringLength(6, MinimumLength = 6)]` | idem |
| Charset enforcement | **none** — `"ABCDEF"` and `"      "` (after binding) both pass | — |
| Sanitization in service | none — code passed verbatim to repo | [`DeviceService.cs:202`](../POS.Services/Service/DeviceService.cs#L202) |
| DB comparison | `WHERE "Code" = @code` (case-sensitive on PostgreSQL) | [`DeviceActivationCodeRepository.cs:18`](../POS.Repository/Repository/DeviceActivationCodeRepository.cs#L18) |

### 2.3 Endpoint Surface

| Method | Route | Auth | In scope |
|---|---|---|---|
| POST | `/api/Device/activate` | `[AllowAnonymous]` | ✅ regex tightened, rate limiter applied |
| POST | `/api/Device/generate-code` | `Owner,Manager` | ❌ out of scope (see §11) |
| POST | `/api/Device/setup` | `[AllowAnonymous]` | ❌ unchanged |

### 2.4 Rate-Limiter Infrastructure (Existing)

[`Program.cs:184-200`](../POS.API/Program.cs#L184-L200) already wires `AddRateLimiter` and registers two **un-partitioned fixed-window** policies (`RegistrationPolicy`, `PublicInvoicingPolicy`, both 5/min, queue 0). `app.UseRateLimiter()` is in the pipeline at [`Program.cs:259`](../POS.API/Program.cs#L259). **No `ForwardedHeadersMiddleware` is registered**, so any IP-partitioned policy added today would collapse to the proxy IP under Render.

### 2.5 Persistence Column

[`DeviceActivationCode.Code`](../POS.Domain/Models/DeviceActivationCode.cs#L8) is declared as `string` with no `[Required]` / `[MaxLength]`, so EF emitted a `text NOT NULL` (or unbounded `nvarchar`) column. Tightening to `nvarchar(6)` is safe under the zero-tech-debt rule once legacy in-flight codes are reaped.

---

## 3. Domain Model Changes

### 3.1 New Helper — `SecureCodeAlphabet`

| Property | Value |
|---|---|
| File | [`POS.Domain/Helpers/SecureCodeAlphabet.cs`](../POS.Domain/Helpers/SecureCodeAlphabet.cs) (new) |
| Visibility | `public static class` |
| Field `Chars` | `"ABCDEFGHJKMNPQRSTVWXYZ23456789"` (30 characters) |
| Field `Length` | `const int` = `6` |
| Invariant (XML doc) | *"The generator uses 5-bit rejection sampling against `Chars.Length` (currently 30): each random byte is masked with `0x1F`; bytes whose result is `≥ Chars.Length` are discarded and re-drawn. The alphabet may grow up to 32 characters without algorithmic changes; growing beyond 32 requires switching to a 6-bit mask. Shrinking is always safe but increases the rejection ratio."* |

### 3.2 `DeviceActivationCode` Tightening

| Property | Before | After | Rationale |
|---|---|---|---|
| `Code` | `string` (no annotations) | `[Required]` + `[MaxLength(6)]` + `[MinLength(6)]` | Aligns the C# model with the existing `character varying(6) NOT NULL` schema (already in place since [`20260327204815_AddDeviceActivationCodes`](../POS.Repository/Migrations/20260327204815_AddDeviceActivationCodes.cs)). `[MinLength]` adds a runtime validation floor; EF does not translate it to a SQL CHECK. |
| All other fields | — | unchanged | Refactor scope is the code itself. |

### 3.3 EF Migration

| Migration | Operation | Notes |
|---|---|---|
| `InvalidateLegacyDeviceActivationCodes` | `Sql("UPDATE \"DeviceActivationCodes\" SET \"IsUsed\" = true, \"UsedAt\" = NOW() WHERE \"IsUsed\" = false;")` | **No schema diff** — the column was already `character varying(6) NOT NULL` from inception (see §3.2). The migration exists purely to retire legacy numeric codes that cannot satisfy the new regex. Applied automatically on startup via `db.Database.Migrate()` ([`Program.cs:221`](../POS.API/Program.cs#L221)). `Down()` is intentionally a no-op — un-consuming previously pending codes is not a recovery path we want to encourage. |

---

## 4. Repository Layer

### 4.1 No Method Signatures Change

| Method | Signature | Contract change |
|---|---|---|
| `GetByCodeAsync(string code)` | unchanged | `code` is now contractually `[A-HJKMNP-TV-Z2-9]{6}` uppercased — service-side guarantee, repo stays naive |
| `CodeExistsAsync(string code)` | unchanged | idem |
| `GetByCodeForUpdateAsync(string code)` | unchanged | idem; `FOR UPDATE` lock preserved |
| `CountPendingByModeAsync` / `GetPendingByTargetAsync` / `ListPendingByBusinessAsync` | unchanged | not on the activation hot path |

### 4.2 Architectural Decision

The sanitization (`Trim().ToUpper()`) lives in the **service**, not the repo. Repos remain persistence-only by project convention; pulling normalization into the repo would entangle data access with input policy.

---

## 5. Service Layer

### 5.1 `IDeviceService` — No Public Surface Changes

The interface contract (`GenerateActivationCodeAsync`, `ActivateAndRegisterDeviceAsync`, etc.) is preserved. Controller code does not change beyond the rate-limit attribute and the DTO annotation.

### 5.2 `DeviceService` Internal Changes

| Change | Type | Detail |
|---|---|---|
| Delete `private static readonly Random _random = new();` | DELETE | Field becomes orphaned after the generator swap; under zero-tech-debt rule, remove rather than keep. |
| Add `private static string GenerateSecureActivationCode()` | NEW | Pure helper. No DI. CSPRNG-backed (`RandomNumberGenerator.Fill`). Returns 6 chars from `SecureCodeAlphabet.Chars`. |
| Modify `GenerateActivationCodeAsync` (collision loop, [`DeviceService.cs:142-153`](../POS.Services/Service/DeviceService.cs#L142-L153)) | MODIFY | Replace `_random.Next(...)` with `GenerateSecureActivationCode()`. Retain the `attempts > 10` cap and the existing `ValidationException` message. |
| Modify `ActivateAndRegisterDeviceAsync` (entry, [`DeviceService.cs:197`](../POS.Services/Service/DeviceService.cs#L197)) | MODIFY | First statement: `code = code?.Trim().ToUpperInvariant() ?? string.Empty;`. All subsequent reads (`GetByCodeAsync`, `GetByCodeForUpdateAsync`) consume the normalized form. |

### 5.3 `GenerateSecureActivationCode` Specification

| Property | Value |
|---|---|
| Visibility | `private static` |
| Signature | `string GenerateSecureActivationCode()` |
| Algorithm | (a) Allocate `Span<char> result = stackalloc char[Length]`. (b) For each `i` in `0..Length-1`, loop: pull one byte via `RandomNumberGenerator.Fill(singleByteSpan)`, compute `index = singleByte & 0x1F`, and break out only when `index < Chars.Length`. Assign `result[i] = Chars[index]`. (c) `return new string(result);` |
| Bias | None — rejection sampling discards bytes whose 5-bit value exceeds `Chars.Length - 1`. Each accepted byte is one of 30 equally-likely outcomes. |
| Thread-safety | `RandomNumberGenerator.Fill` is documented thread-safe. |
| Allocations | One `string` (the return). The char buffer is stack-allocated; the per-byte CSPRNG draws use a 1-byte stack buffer. |
| Expected CSPRNG calls per code | ~6.4 bytes (6 chars × `32/30` acceptance ratio). Cold path; cost is negligible. |

### 5.4 Behavioral Changes Visible to Callers

| Concern | Behavior |
|---|---|
| Code format returned by `GenerateActivationCodeAsync` | 6 chars from Crockford alphabet (uppercase) instead of 6 digits |
| Activation accepts mixed-case input | Yes — DTO regex uses `(?i)`; service uppercases before lookup |
| Activation accepts surrounding whitespace | Yes — service trims before lookup |
| Existing error messages (`"Invalid activation code"`, `"Activation code has already been used"`, `"Activation code has expired"`) | Unchanged verbatim |

---

## 6. Controller & DTO Changes

### 6.1 `ActivateDeviceRequest` DTO

| Property | Before | After |
|---|---|---|
| `Code` | `[Required]` + `[StringLength(6, MinimumLength = 6)]` | `[Required]` + `[RegularExpression("(?i)^[A-HJKMNP-TV-Z2-9]{6}$", ErrorMessage = "Activation code must be exactly 6 characters from the safe alphabet (A-Z and 2-9, excluding 0, O, 1, I, L, U).")]` |
| `DeviceUuid` | `[Required]` + `[MaxLength(100)]` | unchanged |

### 6.2 `DeviceController.Activate` Endpoint

| Aspect | Before | After |
|---|---|---|
| Route | `POST /api/Device/activate` | unchanged |
| Auth | `[AllowAnonymous]` | unchanged |
| Rate limiting | none | `[EnableRateLimiting("DeviceActivationPolicy")]` |
| 200 response shape | `ActivateDeviceResponse` | unchanged |
| 400 triggers | service-layer validation | service-layer + DTO regex (new fail-fast path) |
| 429 response | not emitted today | new — body empty, `Retry-After: <seconds>` header |

### 6.3 Endpoints Explicitly NOT Modified

| Endpoint | Reason |
|---|---|
| `POST /api/Device/generate-code` | Out of scope (§11). Authenticated; abuse vector is internal. |
| `POST /api/Device/setup` | Email/password flow; orthogonal. |
| All `DevicesController` endpoints | Device management surface, untouched by this refactor. |

### 6.4 `Program.cs` Changes

| Block | Operation | Detail |
|---|---|---|
| Forwarded Headers (NEW section) | `builder.Services.Configure<ForwardedHeadersOptions>(...)` | `ForwardedHeaders = XForwardedFor \| XForwardedProto`. `KnownNetworks.Clear()` and `KnownProxies.Clear()` — Render does not expose stable proxy IPs; the container only receives traffic via the platform proxy, so blind trust of `X-Forwarded-For` is acceptable. |
| Rate Limiter ([`Program.cs:184-200`](../POS.API/Program.cs#L184-L200)) — extend, not replace | `options.AddPolicy("DeviceActivationPolicy", httpContext => RateLimitPartition.GetSlidingWindowLimiter(...))` | Parameters in §7.4. |
| Rate Limiter — extend global options | `options.OnRejected = async (ctx, token) => { ... }` | Read `RetryAfter` metadata from the lease, write to the response header, log a warning with the resolved IP and path. Applies globally to the three policies. |
| Pipeline insert | `app.UseForwardedHeaders()` | Position §6.5. |

### 6.5 Pipeline Order (Post-Refactor)

| # | Middleware | Status |
|---|---|---|
| 1 | `app.Use((ctx, next) => { ctx.Request.EnableBuffering(); … })` | existing (Stripe webhook buffering) |
| 2 | **`app.UseForwardedHeaders()`** | **NEW** — must precede every middleware that reads IP |
| 3 | `app.UseExceptionMiddleware()` | existing |
| 4 | `app.UseSerilogRequestLogging()` | existing — now logs the real client IP |
| 5 | `app.UseSwagger()` / `app.UseSwaggerUI()` (dev) | existing |
| 6 | `app.UseHttpsRedirection()` (prod) | existing |
| 7 | `app.UseCors("AllowFrontend")` | existing |
| 8 | **`app.UseRateLimiter()`** | existing — now sees real IP via (2) |
| 9 | `app.UseAuthentication()` | existing |
| 10 | `app.UseAuthorization()` | existing |
| 11 | `app.MapControllers()` / `app.MapHub<KdsHub>(...)` | existing |

---

## 7. Validations

### 7.1 DTO-Layer (Data Annotations on `ActivateDeviceRequest`)

| Attribute | Field | Message | Trigger |
|---|---|---|---|
| `[Required]` | `Code` | (default ASP.NET) | `Code` null or empty |
| `[RegularExpression("(?i)^[A-HJKMNP-TV-Z2-9]{6}$", ErrorMessage = "...")]` | `Code` | `"Activation code must be exactly 6 characters from the safe alphabet (A-Z and 2-9, excluding 0, O, 1, I, L, U)."` | Any deviation in length OR charset |
| `[Required]` | `DeviceUuid` | (default) | UUID null or empty |
| `[MaxLength(100)]` | `DeviceUuid` | (default) | UUID length > 100 |

> The `(?i)` inline flag is supported natively by .NET regex. `RegularExpressionAttribute` does not expose `RegexOptions.IgnoreCase`, so the inline flag is the canonical way to permit lowercase input in a Data Annotation.

### 7.2 Service-Layer (in `ActivateAndRegisterDeviceAsync`)

| Validation | Message | HTTP outcome |
|---|---|---|
| `code.Trim().ToUpperInvariant()` (sanitization) | — | transparent |
| Code not found | `"Invalid activation code"` | 400 |
| Code already consumed | `"Activation code has already been used"` | 400 |
| Code expired | `"Activation code has expired"` | 400 |
| Plan limit exceeded for the mode | (heritage `PlanLimitExceededException` message) | 403 |

### 7.3 Service-Layer (in `GenerateActivationCodeAsync`)

| Validation | Status |
|---|---|
| Mode validity / Name not empty / Branch tenant match / CashRegister pre-flight | unchanged |
| Collision retry cap | unchanged — still throws `"Unable to generate unique code. Please try again."` after 10 attempts (statistically impossible under 32⁶ space) |

### 7.4 Rate Limiter — `DeviceActivationPolicy`

| Parameter | Value | Justification |
|---|---|---|
| Algorithm | `RateLimitPartition.GetSlidingWindowLimiter` | Avoids the burst boundary of fixed window (10 requests at second 59 + 10 at second 61 = 20 in 2 seconds against a "10/min" intent) |
| Partition key | `httpContext.Connection.RemoteIpAddress?.ToString() ?? IPAddress.None.ToString()` | Real IP via forwarded headers; `IPAddress.None` fallback prevents `NullReferenceException` on edge cases (test harness, malformed requests) |
| `PermitLimit` | `10` | Headroom for multi-terminal restaurants behind shared NAT; tight enough to make brute-forcing 32⁶ impractical |
| `Window` | `TimeSpan.FromMinutes(1)` | One-minute cadence |
| `SegmentsPerWindow` | `6` | Segments of 10 s — balances slide granularity against memory cost |
| `QueueLimit` | `0` | No queueing; reject immediately |
| Status code on rejection | `429` | Inherits `options.RejectionStatusCode` ([`Program.cs:187`](../POS.API/Program.cs#L187)) |

### 7.5 Global `OnRejected` (Affects All Three Policies)

| Concern | Behavior |
|---|---|
| HTTP status | `429 Too Many Requests` |
| Header | `Retry-After: <seconds>` from `lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)`; omit if metadata is unavailable |
| Body | empty (framework default) |
| Logging | `Log.Warning("Rate limit hit for {IpAddress} on {Path}", …)` for security observability |

---

## 8. End-to-End Flow

```
                  ┌─────────────────────────────────────────────┐
                  │  CLIENT (Frontend Setup Screen)             │
                  │  POST /api/Device/activate                  │
                  │  Body: { code: " ab23x9 ", deviceUuid:"…" } │
                  └──────────────────┬──────────────────────────┘
                                     │ HTTPS
                                     ▼
              ┌──────────────────────────────────────────────────┐
              │  RENDER PROXY (sets X-Forwarded-For = real IP)   │
              └──────────────────┬───────────────────────────────┘
                                 ▼
   ┌──────────────────────────────────────────────────────────────────────┐
   │  ASP.NET PIPELINE                                                    │
   │                                                                      │
   │  (1) UseForwardedHeaders                                             │
   │      └─► RemoteIpAddress = real client IP                            │
   │                                                                      │
   │  (2) UseRateLimiter (DeviceActivationPolicy)                         │
   │      Partition: real IP (or IPAddress.None fallback)                 │
   │      Sliding window: 10 / 60 s / 6 segments                          │
   │      ├─[exceeded]─► 429  +  Retry-After: <s>                         │
   │      │              Log.Warning(IP, Path)                            │
   │      ▼ [allowed]                                                     │
   │                                                                      │
   │  (3) MVC Model Binding + DataAnnotations                             │
   │      [Required] + [RegularExpression( (?i)Crockford{6} )]            │
   │      ├─[invalid]─► 400 ModelState                                    │
   │      │              "Activation code must be exactly 6 chars …"     │
   │      ▼ [valid]                                                       │
   │                                                                      │
   │  (4) DeviceController.Activate                                       │
   │                                                                      │
   │  (5) DeviceService.ActivateAndRegisterDeviceAsync(code, deviceUuid)  │
   │      │                                                               │
   │      ├─► code = code.Trim().ToUpperInvariant()        [SANITIZE]    │
   │      │                                                               │
   │      ├─► repo.GetByCodeAsync(code)                    [PRE-VALIDATE]│
   │      │   ├─ null    → ValidationException "Invalid…"   → 400        │
   │      │   ├─ IsUsed  → ValidationException "…used"      → 400        │
   │      │   └─ expired → ValidationException "…expired"   → 400        │
   │      │                                                               │
   │      ├─► EnforceDeviceLimitsAsync(...)                [PLAN GATE]   │
   │      │   └─ exceeded → PlanLimitExceededException      → 403        │
   │      │                                                               │
   │      ├─► BeginTransaction                                            │
   │      │   ├─ GetByCodeForUpdateAsync(code)             [LOCK + RECHECK]
   │      │   ├─ Upsert Device                                            │
   │      │   ├─ Mark code IsUsed = true                                  │
   │      │   ├─ Optional: link CashRegister                              │
   │      │   ├─ IssueDeviceToken                                         │
   │      │   └─ Commit                                                   │
   │      │                                                               │
   │      ▼                                                               │
   │  (6) Return 200 + ActivateDeviceResponse { DeviceToken, … }          │
   └──────────────────────────────────────────────────────────────────────┘


   GENERATION SIDE (existing flow — only the random source changes):

   ┌────────────────────────────────────────────────────────────────────┐
   │  DeviceService.GenerateActivationCodeAsync                         │
   │                                                                    │
   │  (existing) Validate mode/name/branch/tenant/cashRegister          │
   │  (existing) Begin transaction + hygiene + enforce limits           │
   │                                                                    │
   │  do {                                                              │
   │     code = GenerateSecureActivationCode()  ◄── NEW HELPER          │
   │     │     ┌─────────────────────────────────────────┐              │
   │     │     │ for i in 0..5:                          │              │
   │     │     │   do {                                  │              │
   │     │     │     RandomNumberGenerator.Fill(buf1)    │              │
   │     │     │     idx = buf1[0] & 0x1F   (5 bits)     │              │
   │     │     │   } while (idx >= Chars.Length /*30*/)  │              │
   │     │     │   result[i] = Chars[idx]                │              │
   │     │     │ return new string(result)               │              │
   │     │     └─────────────────────────────────────────┘              │
   │     attempts++                                                     │
   │  } while (CodeExistsAsync(code))    [collision-safe loop preserved]│
   │                                                                    │
   │  Insert + commit + return GenerateCodeResponse                     │
   └────────────────────────────────────────────────────────────────────┘
```

---

## 9. Implementation Order

### 9.1 Sequencing (single PR)

| # | Task | Layer | Blocks | Notes |
|---|---|---|---|---|
| 1 | Create `SecureCodeAlphabet` (`Chars`, `Length`, XML doc invariant) | Domain | 2, 5 | Single source of truth |
| 2 | Implement `GenerateSecureActivationCode()` private helper in `DeviceService` | Service | 3 | Pure, static, CSPRNG-backed |
| 3 | Swap `_random.Next(...)` for `GenerateSecureActivationCode()` in `GenerateActivationCodeAsync` | Service | 8 | Preserve collision-safe loop |
| 4 | Insert `code = code?.Trim().ToUpperInvariant() ?? string.Empty;` as first statement of `ActivateAndRegisterDeviceAsync` | Service | — | Before any repo call |
| 5 | Replace `[StringLength]` with `[RegularExpression(...)]` on `ActivateDeviceRequest.Code` | API | — | Exact `ErrorMessage` per §7.1 |
| 6 | Tighten `DeviceActivationCode.Code` to `[Required]` + `[MaxLength(6)]` + `[MinLength(6)]` (model-only — no schema change) | Domain | 7 | — |
| 7 | Generate EF migration `InvalidateLegacyDeviceActivationCodes` and inject the SQL cleanup statement into `Up()` | Repository | (deploy) | `dotnet ef migrations add InvalidateLegacyDeviceActivationCodes --project POS.Repository --startup-project POS.API`. EF generates an empty migration body (no schema diff, see §3.3); we hand-edit `Up()` to add the single `migrationBuilder.Sql(...)` call from §9.2. |
| 8 | Delete `private static readonly Random _random` field in `DeviceService` | Service | — | Verify no other references |
| 9 | Configure `ForwardedHeadersOptions` in `Program.cs` (clear `KnownNetworks` + `KnownProxies`) | API | 10 | — |
| 10 | Insert `app.UseForwardedHeaders()` as first active middleware in pipeline | API | 12 | Position §6.5 |
| 11 | Add `DeviceActivationPolicy` to `AddRateLimiter` (sliding, partitioned by IP, params §7.4) | API | 13 | — |
| 12 | Add global `options.OnRejected` with `Retry-After` header + warning log | API | — | Benefits all three policies |
| 13 | Apply `[EnableRateLimiting("DeviceActivationPolicy")]` to `DeviceController.Activate` | API | — | — |
| 14 | (Folded into Step 7 — the SQL cleanup ships inside the migration) | — | — | See §9.2 |
| 15 | Migration auto-applies on startup via `db.Database.Migrate()` ([`Program.cs:221`](../POS.API/Program.cs#L221)) | DB | — | No manual step. Both the SQL cleanup and any future schema changes flow through this path. |

### 9.2 SQL Cleanup (Embedded in Migration)

| Aspect | Value |
|---|---|
| When | Applied automatically on app startup via `db.Database.Migrate()` ([`Program.cs:221`](../POS.API/Program.cs#L221)), as part of the `InvalidateLegacyDeviceActivationCodes` migration. |
| Statement | `migrationBuilder.Sql("UPDATE \"DeviceActivationCodes\" SET \"IsUsed\" = true, \"UsedAt\" = NOW() WHERE \"IsUsed\" = false;");` (sole statement in the migration's `Up()`). |
| Effect | All in-flight legacy numeric codes (which contain `0` and `1` — excluded from the new alphabet) are marked consumed. Operators with a pending generation must re-issue post-deploy. |
| Audit trail | Preserved (`Code`, `CreatedBy`, `CreatedAt` remain). |
| Atomicity | Single migration → single transaction → idempotent across re-runs (EF's `__EFMigrationsHistory` table guarantees one-shot semantics). |
| Alternative considered | (a) Pre-deploy manual script — rejected: fragile, requires human in the loop. (b) `DELETE` instead of `UPDATE` — rejected: discards audit history without benefit. |

### 9.3 Rollback Plan

| Scenario | Action |
|---|---|
| Critical bug post-deploy | `git revert` the PR, then `dotnet ef database update <previous-migration> --project POS.Repository --startup-project POS.API` to roll back the schema. The SQL cleanup needs no reversal — affected rows would have expired naturally within 24 h. |
| Rate limit too aggressive | Hot-fix by raising `PermitLimit` in `Program.cs`. Trivial change, no migration. |

---

## 10. Acceptance Criteria

| # | Criterion | Verification |
|---|---|---|
| AC-1 | Generated codes consist exclusively of `SecureCodeAlphabet.Chars` | Unit test: generate 10⁵ codes, all match `^[A-HJKMNP-TV-Z2-9]{6}$` |
| AC-2 | Distribution of characters is statistically uniform | Chi-square goodness-of-fit over 10⁵ generations (per-character bucket counts) |
| AC-3 | DTO rejects codes outside the alphabet with 400 | Integration: `"abc123"` (contains `1`), `"AAAAA0"` (contains `0`), `"AAAAA"` (length 5) all return 400 ModelState |
| AC-4 | Service accepts whitespace and lowercase input | Integration: send `"  abxyz2  "`, expect lookup against `"ABXYZ2"` |
| AC-5 | Rate limiter returns 429 on the 11th request within 60 s from the same IP | Integration: 11 sequential POSTs |
| AC-6 | 429 responses include a numeric `Retry-After` header | Integration: assert header presence and parseability |
| AC-7 | Under simulated proxy, the limiter partitions by `X-Forwarded-For`, not socket IP | Integration: two distinct `X-Forwarded-For` values consume independent buckets |
| AC-8 | `_random` field no longer exists in `DeviceService` | Code review + grep |
| AC-9 | `Code` column is `nvarchar(6) NOT NULL` post-migration | Schema inspection |
| AC-10 | No legacy pending codes survive deploy | `SELECT COUNT(*) FROM "DeviceActivationCodes" WHERE "IsUsed" = false AND "Code" !~ '^[A-HJKMNP-TV-Z2-9]{6}$'` returns `0` |

---

## 11. Out of Scope

| Item | Rationale | Follow-up |
|---|---|---|
| Rate limiting on `POST /api/Device/generate-code` | Endpoint is `[Authorize(Roles="Owner,Manager")]`; abuse vector is internal. Keeping the PR atomic. | Open ticket: *"Throttle generate-code by `BusinessId` claim, 30 req/min"* |
| Audit logging of failed activations with IP + DeviceUuid | Useful for brute-force detection but orthogonal to the hardening contract. | Open ticket: *"Append `Log.Warning` on `Invalid activation code` paths in `DeviceService`"* |
| Per-environment rate-limit tuning via `appsettings.json` | Today the limits are inline in `Program.cs`. | Open ticket: *"Externalize `PermitLimit` / `Window` to configuration"* |

---

## 12. Frontend Implications (Informational, Not Part of This Backend PR)

| Aspect | Expected Angular change |
|---|---|
| Activation input mode | `inputmode="text" autocapitalize="characters"` (was `inputmode="numeric"`) |
| Client-side fail-fast | Mirror the regex `(?i)^[A-HJKMNP-TV-Z2-9]{6}$` to short-circuit invalid input |
| 429 handling | Read `Retry-After` and surface a *"Reintentá en X segundos"* notice |
| Generated-code display | Consider `XXX-XXX` formatting for readability when shown to the admin |

---

**End of BDD-016.**
