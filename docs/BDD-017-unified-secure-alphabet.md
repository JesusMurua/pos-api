# BDD-017 — Unified Secure Code Alphabet (Closing BDD-016 Scope Gap)
**Fase:** 21 | **Estado:** Proposed | **Fecha:** 2026-04-30
**Documentos relacionados:**
- [BDD-016-device-activation-code-hardening.md](BDD-016-device-activation-code-hardening.md) — Original hardening that introduced `SecureCodeAlphabet` (originally named `DeviceActivationAlphabet`) and the CSPRNG generator. Scope was limited to Device activation; this BDD closes the parallel surface.
- [AUDIT-049-device-activation-register-linking-ux.md](AUDIT-049-device-activation-register-linking-ux.md) — Earlier audit on the device-register linking flows.

---

## 1. Executive Summary

### 1.1 Problem Statement

Post-BDD-016, a real activation code emitted by the back office was observed containing the character `L` (`85SL5J`). Investigation revealed it did **not** come from `DeviceService.GenerateSecureActivationCode` — that helper is provably immune to forbidden characters by construction. The leak originated in a parallel, undocumented code-generation surface: [`CashRegisterService.GenerateRandomLinkCode`](../POS.Services/Service/CashRegisterService.cs) (originally lines 560-568), which shipped its own ad-hoc CSPRNG-backed generator with a hardcoded **32-character** alphabet (`"ABCDEFGHJKLMNPQRSTUVWXYZ23456789"`) that **includes `L` and `U`** — both forbidden under BDD-016's secure 30-character alphabet.

The two systems coexisted invisibly because they serve adjacent but distinct purposes:

| Concept | Purpose | Surface |
|---|---|---|
| `DeviceActivationCode` | Bootstrap a fresh, anonymous terminal into a tenant | `POST /api/Device/activate` |
| `CashRegisterLinkCode` | Bind an already-paired device to a specific cash register (Mode B) | `POST /api/CashRegister/registers/redeem-link-code` |

To an operator, both render as 6-character uppercase alphanumeric codes. The asymmetry was a scope omission in BDD-016: the audit at the time grepped `DeviceService` and `DeviceController` and never reached `CashRegisterService`. Compounding the issue, the redeem DTO ([`RedeemLinkCodeRequest`](../POS.API/Controllers/CashRegisterController.cs)) used the same lax `[StringLength(6, MinimumLength = 6)]` validation that BDD-016 had already replaced for `ActivateDeviceRequest` — the same anti-pattern, undeleted.

### 1.2 Proposed Solution

Unify both surfaces under one source of truth:

1. **Rename** `DeviceActivationAlphabet` → `SecureCodeAlphabet` to reflect its now-shared role. Constant `Chars` and `Length` are unchanged (`"ABCDEFGHJKMNPQRSTVWXYZ23456789"`, 30 chars; `6`).
2. **Extract** the CSPRNG + 5-bit rejection-sampling logic from `DeviceService` into a new shared helper [`POS.Services/Helpers/SecureCodeGenerator.cs`](../POS.Services/Helpers/SecureCodeGenerator.cs) with a single public method `Generate(int length = 6)`. The method **hardcodes** `SecureCodeAlphabet.Chars` rather than accepting an alphabet parameter — this prevents the next caller from re-introducing a parallel charset.
3. **Refactor** both `DeviceService.GenerateActivationCodeAsync` and `CashRegisterService.GenerateLinkCodeAsync` to call `SecureCodeGenerator.Generate()`.
4. **Delete** `CashRegisterService.GenerateRandomLinkCode` — the legacy 32-char generator with `bytes[i] % charset.Length` (modulo, accidentally bias-free only because `256 % 32 == 0`).
5. **Tighten** `RedeemLinkCodeRequest.Code` with the same regex contract as `ActivateDeviceRequest.Code` (`(?i)^[A-HJKMNP-TV-Z2-9]{6}$` + the BDD-016 error message wording, adapted to "link code").
6. **Sanitize** the input in `CashRegisterService.RedeemLinkCodeAsync` with `code = code?.Trim().ToUpperInvariant() ?? string.Empty;` as STEP 0, mirroring `DeviceService.ActivateAndRegisterDeviceAsync`.

### 1.3 Expected Outcome / Impact

- **Single charset across the API.** Any future 6-char alphanumeric code generation must consume `SecureCodeGenerator.Generate()`. The `SecureCodeAlphabet` is the only legitimate source.
- **Zero forbidden characters in any code.** `I, L, O, U, 0, 1` cannot be emitted by either surface.
- **Bias-free regardless of alphabet cardinality.** Rejection sampling guarantees uniformity even if the alphabet shrinks/grows in the future (subject to staying ≤ 32 — see invariant in BDD-016 §3.1).
- **DTO symmetry restored.** Both redemption endpoints (`/activate`, `/redeem-link-code`) now apply identical fail-fast contract validation.
- **Architectural lesson captured.** This BDD documents the scope-gap antipattern explicitly so future security/contract refactors include a "parallel surface sweep" checkpoint.

---

## 2. Current State Analysis

### 2.1 Surfaces Generating 6-Char Codes Today

| Surface | Generator (pre-refactor) | Alphabet | Cardinality | Bias |
|---|---|---|---|---|
| `DeviceService.GenerateActivationCodeAsync` | `DeviceService.GenerateSecureActivationCode` | `DeviceActivationAlphabet.Chars` | 30 | None (rejection sampling) |
| `CashRegisterService.GenerateLinkCodeAsync` | `CashRegisterService.GenerateRandomLinkCode` (lines 560-568) | hardcoded `"ABCDEFGHJKLMNPQRSTUVWXYZ23456789"` | 32 | None (accidentally — `256 % 32 == 0`) |

### 2.2 DTO Validation Today

| DTO | Validation | Status |
|---|---|---|
| `ActivateDeviceRequest.Code` | `[Required] + [RegularExpression("(?i)^[A-HJKMNP-TV-Z2-9]{6}$", ErrorMessage = "...")]` | ✅ Hardened in BDD-016 |
| `RedeemLinkCodeRequest.Code` | `[Required] + [StringLength(6, MinimumLength = 6)]` | ❌ Same lax pattern that BDD-016 retired for the activation flow |

### 2.3 Service-Layer Sanitization Today

| Service method | Sanitization |
|---|---|
| `DeviceService.ActivateAndRegisterDeviceAsync` | ✅ `code = code?.Trim().ToUpperInvariant() ?? string.Empty;` (STEP 0) |
| `CashRegisterService.RedeemLinkCodeAsync` | ❌ Code passed verbatim to `GetByCodeForUpdateAsync` |

---

## 3. Refactor Plan

### 3.1 Domain Layer — Rename

| Operation | Before | After |
|---|---|---|
| File rename (`git mv`) | [`POS.Domain/Helpers/DeviceActivationAlphabet.cs`](../POS.Domain/Helpers/DeviceActivationAlphabet.cs) | [`POS.Domain/Helpers/SecureCodeAlphabet.cs`](../POS.Domain/Helpers/SecureCodeAlphabet.cs) |
| Class name | `public static class DeviceActivationAlphabet` | `public static class SecureCodeAlphabet` |
| `Chars` | `"ABCDEFGHJKMNPQRSTVWXYZ23456789"` | unchanged |
| `Length` | `6` | unchanged |
| XML doc | mentions activation specifically | generalized to "any short, human-readable alphanumeric code" |

### 3.2 Services Layer — New Shared Helper

| Property | Value |
|---|---|
| File | [`POS.Services/Helpers/SecureCodeGenerator.cs`](../POS.Services/Helpers/SecureCodeGenerator.cs) (new — folder created) |
| Visibility | `public static class SecureCodeGenerator` |
| Public method | `public static string Generate(int length = SecureCodeAlphabet.Length)` |
| Algorithm | Same as the original `DeviceService.GenerateSecureActivationCode`: CSPRNG (`RandomNumberGenerator.Fill`) + 5-bit rejection sampling against `SecureCodeAlphabet.Chars.Length`, per-byte loop |
| Hardcoded alphabet | **Yes** — by design. The method does **not** accept an alphabet parameter, preventing re-introduction of a parallel charset |
| XML doc explicitly mentions | CSPRNG-backed, bias-free via rejection sampling, thread-safe |

### 3.3 Service Refactor — `DeviceService`

| Change | Detail |
|---|---|
| Delete `GenerateSecureActivationCode()` (private static) | Logic now lives in the shared helper |
| Replace call in `GenerateActivationCodeAsync` (collision loop) | `code = GenerateSecureActivationCode()` → `code = SecureCodeGenerator.Generate()` |
| Remove `using System.Security.Cryptography;` | No longer needed in this file |
| Add `using POS.Services.Helpers;` (if needed) | For `SecureCodeGenerator` reference |
| Sanitization in `ActivateAndRegisterDeviceAsync` | Unchanged — remains as STEP 0 |

### 3.4 Service Refactor — `CashRegisterService`

| Change | Detail |
|---|---|
| Delete `GenerateRandomLinkCode()` (private static) | Replaced by shared helper |
| Replace call in `GenerateLinkCodeAsync` (collision loop) | `code = GenerateRandomLinkCode()` → `code = SecureCodeGenerator.Generate()` |
| Add `using POS.Services.Helpers;` | For `SecureCodeGenerator` reference |
| Add sanitization to `RedeemLinkCodeAsync` (NEW) | First statement: `code = code?.Trim().ToUpperInvariant() ?? string.Empty;` — mirrors `DeviceService.ActivateAndRegisterDeviceAsync` |

### 3.5 DTO Tightening — `RedeemLinkCodeRequest`

| Property | Before | After |
|---|---|---|
| `Code` | `[Required] + [StringLength(6, MinimumLength = 6)]` | `[Required] + [RegularExpression("(?i)^[A-HJKMNP-TV-Z2-9]{6}$", ErrorMessage = "Link code must be exactly 6 characters from the safe alphabet (A-Z and 2-9, excluding 0, O, 1, I, L, U).")]` |

> Wording mirrors BDD-016's activation-code error message verbatim, swapping "Activation" for "Link" so the operator sees a domain-correct hint while the contract remains identical.

---

## 4. Verification

### 4.1 Build

`dotnet build pos-api.slnx` → 0 errors. Pre-existing warnings unrelated to this PR remain.

### 4.2 Generation Loop (Temporary Console Probe)

A short-lived `tmp_verify/` console project asserts the contract end-to-end:

| Probe | Iterations | Assertion |
|---|---|---|
| `SecureCodeGenerator.Generate()` directly | 20,000 | Each output: `length == 6` AND `Regex.IsMatch(code, "^[A-HJKMNP-TV-Z2-9]{6}$")` AND no char in `{I, L, O, U, 0, 1}` |
| Distribution check | 20,000 | Per-character bucket count within ±5% of expected uniform mean (`120,000 / 30 = 4,000` per bucket) |

The probe project is deleted after the run. Results are logged in the PR description.

### 4.3 DTO Symmetry

Manual integration test (curl/Postman) sending lowercase, uppercase, mixed, whitespace-padded, and forbidden-char inputs to both `/activate` and `/redeem-link-code`. Both endpoints return identical 400 ModelState messages on forbidden chars and accept lowercase/whitespace via the `(?i)` flag + service-layer normalization.

---

## 5. Implementation Order

| # | Task | Layer | Dependency |
|---|---|---|---|
| 1 | Create this BDD-017 doc | Docs | — |
| 2 | Update BDD-016 references (`DeviceActivationAlphabet` → `SecureCodeAlphabet`) | Docs | — |
| 3 | `git mv DeviceActivationAlphabet.cs SecureCodeAlphabet.cs` and rename the class | Domain | 2 |
| 4 | Create `POS.Services/Helpers/SecureCodeGenerator.cs` (folder + file) | Services | 3 |
| 5 | Refactor `DeviceService` to consume `SecureCodeGenerator.Generate()` and delete its private helper | Services | 4 |
| 6 | Refactor `CashRegisterService.GenerateLinkCodeAsync` similarly; delete `GenerateRandomLinkCode` | Services | 4 |
| 7 | Add sanitization to `CashRegisterService.RedeemLinkCodeAsync` | Services | 6 |
| 8 | Tighten `RedeemLinkCodeRequest.Code` regex | API | — |
| 9 | `dotnet build` — expect 0 errors | — | 5, 6, 7, 8 |
| 10 | Create `tmp_verify/` console project, run 20K iteration loop, capture results | — | 9 |
| 11 | Delete `tmp_verify/` and confirm `git status` is clean | — | 10 |

---

## 6. Acceptance Criteria

| # | Criterion | Verification |
|---|---|---|
| AC-1 | No production code references the old `DeviceActivationAlphabet` name | `grep -r DeviceActivationAlphabet POS.*` returns 0 hits. (Docs intentionally retain the historical name for rename traceability — BDD-016 breadcrumb header and BDD-017 executive summary.) |
| AC-2 | No `Random` instance is used for code generation in any service | `grep -r "new Random" POS.Services` returns 0 hits |
| AC-3 | `SecureCodeGenerator.Generate()` produces only chars in `SecureCodeAlphabet.Chars` | Verification loop §4.2 |
| AC-4 | `CashRegisterService.GenerateLinkCodeAsync` and `DeviceService.GenerateActivationCodeAsync` both invoke `SecureCodeGenerator.Generate()` | Code review |
| AC-5 | `RedeemLinkCodeRequest.Code` rejects `"abc012"` (contains `0`/`1`) and accepts `"abxyz2"` (lowercase, valid charset) | Manual integration test |
| AC-6 | `CashRegisterService.RedeemLinkCodeAsync` accepts whitespace-padded and lowercased input | Manual integration test |
| AC-7 | `tmp_verify/` does not exist after the PR | `git status` is clean of `tmp_verify` artifacts |

---

## 7. Out of Scope

| Item | Reason | Follow-up |
|---|---|---|
| Rate-limit on `POST /api/CashRegister/registers/redeem-link-code` | This endpoint is `[Authorize]`, so abuse vector is internal (a logged-in device). Mirror of BDD-016's deferred `/generate-code` rate-limit. | Open ticket: *"Apply `DeviceActivationPolicy` (or sibling) to redeem-link-code"* |
| Audit log of failed redeem attempts | Useful for detecting compromised device tokens probing codes; orthogonal to the unification contract. | Open ticket |
| Rename `RedeemLinkCodeRequest` → something more domain-specific | Cosmetic, no functional impact. | Optional |

---

## 8. Architectural Lesson Captured

> When introducing a hardening / contract refactor, sweep all parallel surfaces with the same shape (e.g., "all services that generate short alphanumeric codes") before declaring scope. The BDD-016 audit only inspected `DeviceService` because the task was framed as "fix the device activation flow"; it missed `CashRegisterService` because that surface had no observable defect at the time. Subsequent feature work (the linking flows in commit `b75e4b3`) was free to invent a parallel charset because no shared abstraction existed yet.
>
> **Heuristic for future refactors:** before treating a refactor as complete, run a structural grep for "things that look like the thing we just changed" (e.g., `grep -r "RandomNumberGenerator.Fill" POS.Services` after touching a CSPRNG path). If it returns more than one hit, document why each is independently correct or fold them into the refactor.

---

**End of BDD-017.**
