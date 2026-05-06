# AUDIT-022 — Customer Creation API Contract

**Date:** 2026-05-04
**Scope:** `POST /api/customers`
**Trigger:** Frontend received `400 Bad Request: {"FirstName":["The FirstName field is required"]}` when posting a new customer payload.
**Status:** Read-only audit. No code changes proposed.

---

## 1. Endpoint Under Audit

| Property | Value |
|----------|-------|
| Route | `POST /api/customers` |
| Controller | [CustomersController.cs](../POS.API/Controllers/CustomersController.cs) |
| Action | [`Create`](../POS.API/Controllers/CustomersController.cs#L80-L96) |
| Authorization | `[Authorize]` + `[Authorize(Roles = "Owner,Manager")]` |
| Body Binding | `[FromBody] CreateCustomerRequest` |
| Validation Gate | `if (!ModelState.IsValid) return BadRequest(ModelState);` |

---

## 2. Request DTO — `CreateCustomerRequest`

Defined in the same file at [CustomersController.cs:266-286](../POS.API/Controllers/CustomersController.cs#L266-L286).

```csharp
public class CreateCustomerRequest
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = null!;

    [MaxLength(100)]
    public string? LastName { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>Maximum credit limit in cents. 0 = no limit.</summary>
    public int CreditLimitCents { get; set; }

    [MaxLength(500)]
    public string? Notes { get; set; }
}
```

### 2.1 Field Contract Matrix

| Field | Type | Required | Max Length | Default | Notes |
|-------|------|:--------:|:----------:|:-------:|-------|
| `FirstName` | `string` | ✅ **YES** | 100 | — | `[Required]` — must be present and non-empty |
| `LastName` | `string?` | ❌ No | 100 | `null` | Optional — can be omitted or null |
| `Phone` | `string?` | ❌ No | 20 | `null` | Optional |
| `Email` | `string?` | ❌ No | 255 | `null` | Optional. **No `[EmailAddress]` validator** — any string ≤255 is accepted |
| `CreditLimitCents` | `int` | ❌ No | — | `0` | Optional. `0` = no limit (per XML doc) |
| `Notes` | `string?` | ❌ No | 500 | `null` | Optional |

### 2.2 Fields NOT accepted by the DTO

The frontend MUST NOT send these — they will be silently ignored by the model binder, but they are not part of the contract:

- ❌ `FullName` / `Name` / `DisplayName`
- ❌ `BusinessId` (resolved server-side from JWT via `BaseApiController.BusinessId`)
- ❌ `Id` (assigned by the database on create)
- ❌ `Active` / `IsActive` (handled by service layer)
- ❌ `BalanceCents`, `LoyaltyPoints` (ledger-derived, not settable on create)

---

## 3. Validation Logic

### 3.1 Where validation runs

1. **Model binding** (ASP.NET) — populates `CreateCustomerRequest` from JSON body. Property name match is **case-insensitive** by default.
2. **DataAnnotations** — `[Required]`, `[MaxLength]` evaluated, populating `ModelState`.
3. **Controller gate** — `if (!ModelState.IsValid) return BadRequest(ModelState);` returns the 400 with the field-keyed error dictionary the frontend is seeing.
4. **Service layer** — additional business rules (e.g. duplicate phone) run after DTO validation passes; those produce different error shapes.

### 3.2 Is there any "FullName" handling?

**No.** There is no logic anywhere in the controller or DTO that:

- Accepts a single `FullName` / `Name` field.
- Splits a full name into `FirstName` / `LastName`.
- Provides a fallback when `FirstName` is missing.

The API **strictly requires the name to be split**. If the frontend posts a single `fullName: "Juan Pérez"` field, the model binder ignores it and `FirstName` remains `null`, which triggers the exact error observed:

```json
{ "FirstName": ["The FirstName field is required"] }
```

### 3.3 Why the current 400 is happening

The error message text (`"The FirstName field is required"`) is the standard ASP.NET DataAnnotations message for `[Required]` on a string property. This confirms the frontend payload is **not sending `firstName`** (or is sending it as `null` / empty string after trim — though `[Required]` on a non-nullable string fails on `null` before any trimming is involved).

---

## 4. Minimum Valid Payload

```json
{
  "firstName": "Juan"
}
```

Note: ASP.NET's default JSON serializer is **camelCase**, so the frontend should send `firstName` (camelCase). The error response key uses PascalCase (`FirstName`) because `ModelState` keys mirror the C# property names — this is normal and does not indicate the payload casing is wrong.

## 5. Typical Valid Payload

```json
{
  "firstName": "Juan",
  "lastName": "Pérez",
  "phone": "+52 555 123 4567",
  "email": "juan@example.com",
  "creditLimitCents": 50000,
  "notes": "Cliente frecuente"
}
```

---

## 6. Findings Summary

1. **`FirstName` is the ONLY required field** on `CreateCustomerRequest`.
2. **All other fields are optional** (`LastName`, `Phone`, `Email`, `CreditLimitCents`, `Notes`).
3. **The API does NOT accept a combined `FullName` / `Name` field.** Names must be split client-side before posting.
4. **No email format validation** is enforced at the DTO layer — only max length 255.
5. The 400 error the frontend is receiving is consistent with a payload that omits `firstName` or sends it as `null`/missing.

---

## 7. Recommendation for Frontend

To resolve the 400, the frontend form must:

- Send a `firstName` property (camelCase) with a non-empty string value.
- If the UI exposes a single "Nombre completo" input, split it client-side: first token → `firstName`, remaining tokens → `lastName` (or leave `lastName` empty and put the whole thing in `firstName`).

---

**End of audit.** No code modified.
