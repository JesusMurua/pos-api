# AUDIT-024 — Chameleon (Multi-Vertical) Domain Readiness

**Date:** 2026-05-05
**Scope:** `Customer`, `Product`, `Order`, `OrderItem`, plus their EF Core 9 mapping in `ApplicationDbContext`.
**Goal:** Evaluate whether the current domain shape is ready for a "Chameleon" evolution where vertical-specific concerns live in (a) strongly-typed JSON columns mapped via EF Core 9 `ToJson()`/owned-types and/or (b) dedicated entitlement tables (e.g. `CustomerMemberships`).
**Status:** Read-only audit. No code changes proposed.

---

## 1. Vertical Pollution in Generic Entities

### 1.1 `Customer` — leaking gym/membership concerns

[Customer.cs:59-70](../POS.Domain/Models/Customer.cs#L59-L70) defines two strict columns that are exclusively meaningful to the **Gym/Fitness vertical** (or any subscription-based business):

| Column | Type (C#) | Type (Postgres) | Vertical | Source |
|--------|-----------|-----------------|----------|--------|
| `MembershipValidUntil` | `DateTime?` | `timestamp with time zone` | Gym / subscriptions | [migration AddGymVerticalFoundations.cs:26-30](../POS.Repository/Migrations/20260425090358_AddGymVerticalFoundations.cs#L26-L30) |
| `LastPaymentAt` | `DateTime?` | `timestamp with time zone` | Gym / subscriptions / churn analytics | [migration AddGymVerticalFoundations.cs:20-24](../POS.Repository/Migrations/20260425090358_AddGymVerticalFoundations.cs#L20-L24) |

EF mapping (no JSON, no owned-type — just plain columns):
- [ApplicationDbContext.cs:1426-1427](../POS.Repository/ApplicationDbContext.cs#L1426-L1427) — partial filtered index:
  ```csharp
  entity.HasIndex(c => c.MembershipValidUntil)
      .HasFilter("\"MembershipValidUntil\" IS NOT NULL");
  ```

**Diagnosis:** these are **vertical-specific scalar columns sitting on a generic `Customer` aggregate**. A coffee shop, retail store, or restaurant tenant carries unused null columns and an index they will never benefit from. There is **no `CustomerMembership` aggregate** — the model assumes *exactly one* membership per customer, with no period log, no plan/tier, no pause/refund concept (cross-reference: AUDIT-023 §2).

The class XML doc itself acknowledges the vertical bias ("Updated by `ExtendMembershipAsync` when a membership product is sold").

### 1.2 `Customer` — the rest is generic / CRM-shaped

The other fields on `Customer` (`FirstName`, `LastName`, `Phone`, `Email`, `PointsBalance`, `CreditBalanceCents`, `CreditLimitCents`, `Notes`, `IsActive`) are universal CRM/loyalty/credit concerns that legitimately live on the generic aggregate. Not vertical pollution.

### 1.3 `Product` — capability flags, no vertical pollution

[Product.cs](../POS.Domain/Models/Product.cs) carries:
- Universal commerce: `Name`, `PriceCents`, `Description`, `Barcode`, `IsAvailable`, `IsPopular`, `ImageUrl`.
- Inventory toggles: `TrackStock`, `CurrentStock`, `LowStockThreshold` (universal, opt-in).
- Fiscal/SAT: `SatProductCode`, `SatUnitCode`, `IsTaxIncluded` (universal for MX tax compliance, not a vertical).
- Restaurant-leaning: `PrintingDestination` (kitchen vs bar) — arguably a restaurant concern, but it's an enum on a generic property and stays null/default for non-restaurant cases.
- **Vertical extensibility hatch**: `Metadata : string?` ([Product.cs:74](../POS.Domain/Models/Product.cs#L74)) — used today to carry `{"MembershipDurationDays": 30}` for gym memberships.

No vertical-specific scalar fields are leaking onto `Product`.

### 1.4 `Order` — generic, with one capability hatch

[Order.cs](../POS.Domain/Models/Order.cs) has only universal sales/fiscal/sync fields. The restaurant-related `TableId`/`TableName` are nullable opt-ins. Delivery integration fields (`OrderSource`, `ExternalOrderId`, `DeliveryStatus`, `DeliveryCustomerName`, `EstimatedPickupAt`) are arguably "delivery vertical" pollution but they're already nullable enums/strings tied to a documented `OrderSource.Direct` default — they'd be candidates to move into a JSON shape if delivery-platform churn becomes a problem.

`Order` does **not** have its own `Metadata` column (Product/OrderItem/OrderPayment have one — Order does not).

### 1.5 `OrderItem` — has a per-line metadata hatch

[OrderItem.cs:58-63](../POS.Domain/Models/OrderItem.cs#L58-L63):

```csharp
/// <summary>
/// Vertical-specific extensibility payload (JSON) at the line level.
/// Used for item-scoped data that does not belong on the global Order, e.g.
/// {"BeneficiaryCustomerId": 123} for a gym membership purchased on behalf of another customer.
/// </summary>
public string? Metadata { get; set; }
```

Plus a separate raw payload, `ExtrasJson : string?` ([OrderItem.cs:26](../POS.Domain/Models/OrderItem.cs#L26)) — pre-existing field carrying restaurant modifier/extras text. Two unrelated raw-JSON properties on the same entity (`Metadata` vs `ExtrasJson`).

### 1.6 Restaurant-leaning fields on `Branch`

Out of scope for the requested entities, but relevant context: [Branch.cs:37-42](../POS.Domain/Models/Branch.cs#L37-L42) carries `HasKitchen`, `HasTables`, `HasDelivery` — boolean capability toggles. Not strict vertical pollution (they read like feature flags), but they encode a restaurant-shaped worldview. Worth flagging in a Chameleon redesign so that a generic capability matrix could absorb them.

---

## 2. Existing `Metadata` Inventory

### 2.1 Domain-side inventory

| Entity | Property | C# Type | Purpose | Reference |
|--------|----------|---------|---------|-----------|
| `Product` | `Metadata` | `string?` | Vertical-specific config (e.g. `{"MembershipDurationDays": 30}`) | [Product.cs:74](../POS.Domain/Models/Product.cs#L74) |
| `OrderItem` | `Metadata` | `string?` | Per-line vertical payload (e.g. `{"BeneficiaryCustomerId": 123}`) | [OrderItem.cs:63](../POS.Domain/Models/OrderItem.cs#L63) |
| `OrderItem` | `ExtrasJson` | `string?` | Pre-existing raw extras list (modifiers) | [OrderItem.cs:26](../POS.Domain/Models/OrderItem.cs#L26) |
| `OrderPayment` | `PaymentMetadata` | `string?` | Provider-specific JSON (Clip/MercadoPago) | [OrderPayment.cs:31](../POS.Domain/Models/OrderPayment.cs#L31) |
| `SyncOrderItemRequest` (DTO) | `Metadata` | `string?` | Wire-mirror of `OrderItem.Metadata` | [SyncOrderRequest.cs:77](../POS.Domain/Models/SyncOrderRequest.cs#L77) |
| `SyncPaymentRequest` (DTO) | `PaymentMetadata` | `string?` | Wire-mirror of `OrderPayment.PaymentMetadata` | [SyncOrderRequest.cs:116](../POS.Domain/Models/SyncOrderRequest.cs#L116) |
| `MacroCategoryTemplates` (record helper) | `Metadata` | `string?` | Seed-time JSON template | [MacroCategoryTemplates.cs:23](../POS.Domain/Helpers/MacroCategoryTemplates.cs#L23) |

### 2.2 EF / Postgres mapping of every Metadata column

All `Metadata` columns are mapped as **raw, opaque PostgreSQL `text`** — confirmed by [migration AddGymVerticalFoundations.cs:14-18](../POS.Repository/Migrations/20260425090358_AddGymVerticalFoundations.cs#L14-L18):

```csharp
migrationBuilder.AddColumn<string>(
    name: "Metadata",
    table: "Products",
    type: "text",
    nullable: true);
```

**Not** `jsonb`. **Not** `json`. **Not** mapped via `HasColumnType("jsonb")`. **Not** mapped via `OwnsOne`/`ComplexProperty`/`ToJson()`. **Not** validated at the EF layer — schema enforcement is purely the application's responsibility.

Consumption is fully manual:
- Reads use `System.Text.Json.JsonDocument` against the raw string ([OrderService.cs:1655-1662 — `TryReadBeneficiaryCustomerId`](../POS.Services/Service/OrderService.cs#L1655-L1662)).
- Writes are produced as string literals (e.g. seed `"""{"MembershipDurationDays":30}"""` in [MacroCategoryTemplates.cs:70](../POS.Domain/Helpers/MacroCategoryTemplates.cs#L70)).

There is **no shared schema, no DTO, no enum, no validator** governing what may go into a `Metadata` payload. Each call site rolls its own parsing.

### 2.3 EF Core 9 JSON / owned-type usage in the project

A repository-wide search for `ToJson`, `OwnsOne`, `OwnsMany`, `ComplexProperty`, `jsonb`, `json` in `POS.Repository/` returned:

- ❌ **Zero `ToJson()` calls.**
- ❌ **Zero `OwnsOne` / `OwnsMany` / `ComplexProperty` configurations.**
- ❌ **Zero `HasColumnType("jsonb")` / `HasColumnType("json")` declarations.**
- ✅ Only [`AuditInterceptor`](../POS.Repository/Interceptors/AuditInterceptor.cs) uses `System.Text.Json.JsonSerializer.Serialize` — and that is an **interceptor-level** plain-string serialization for audit log payloads, not an EF mapping.

**EF Core 9's first-class JSON capabilities are entirely unused in this project.**

---

## 3. Other Domain Findings Relevant to a Chameleon Move

- **`Order` has no `Metadata` column** — symmetry break vs Product/OrderItem/OrderPayment. If a Chameleon design wants per-order vertical payloads (e.g. delivery platform metadata, gym session check-in), this slot is missing.
- **`Customer` has no `Metadata` column** — vertical extension currently happens through hard scalar columns (`MembershipValidUntil`, `LastPaymentAt`). The pattern used elsewhere (Product/OrderItem) is not applied here.
- **`OrderPayment.PaymentMetadata`** is a textual prior-art for "vendor-specific JSON on a generic aggregate". It pre-dates the gym work, and it's working — i.e. the project already operates with raw-JSON metadata without major incident.
- **No discriminator/inheritance** on any of these entities (no TPH/TPC). All four are concrete, single-table aggregates.
- **`partial class` is used** on `Order`, `OrderItem`, `Product`, `Branch` — vertical-specific extension methods could be added in companion partials without touching these files. Not used today.

---

## 4. Findings Summary

### 4.1 Vertical pollution in `Customer`

Two strictly-typed scalar columns leak the **gym/subscription** vertical onto the generic `Customer` aggregate:

1. `MembershipValidUntil : DateTime?`
2. `LastPaymentAt : DateTime?`

Both are present in the DB as real columns (`timestamp with time zone`) with one filtered index, even for tenants on non-subscription verticals. There is **no `CustomerMembership` table** — the model is single-tier, single-period, in-place-overwritten.

### 4.2 How `Metadata` is currently handled

| Aspect | Current state |
|--------|--------------|
| Where it exists | `Product.Metadata`, `OrderItem.Metadata`, `OrderItem.ExtrasJson`, `OrderPayment.PaymentMetadata` |
| C# type | `string?` (raw nullable string) — **not** a typed class, **not** a `JsonDocument`, **not** an owned entity |
| Postgres column type | `text` — **not** `jsonb`, **not** `json` |
| EF mapping | Plain scalar property. **No `OwnsOne`, no `OwnsMany`, no `ToJson()`, no `ComplexProperty`, no `HasColumnType("jsonb")`** anywhere in the repository |
| Schema/validation | None at the DB or ORM layer — every consumer parses manually with `System.Text.Json.JsonDocument`/`JsonSerializer` |
| Querying | No JSON-path indexes; no LINQ-to-JSON expressions; cannot filter inside `Metadata` from EF |
| EF Core 9 JSON features | **Entirely unused project-wide** |

### 4.3 Readiness verdict

| Dimension | Ready? |
|-----------|:------:|
| Generic entity hygiene (no vertical scalars) | ⚠️ Partial — `Customer` carries gym-only `MembershipValidUntil`/`LastPaymentAt`; `Branch` has restaurant-leaning capability flags |
| Strongly-typed JSON metadata via EF Core 9 | ❌ Not adopted — every metadata column is raw `text`, no `ToJson()` / owned types in use |
| Dedicated entitlement tables (e.g. `CustomerMemberships`) | ❌ Do not exist — membership is denormalized into 2 columns |
| Per-line vertical payload hatch | ✅ Exists (`OrderItem.Metadata`) — but as raw string, not typed |
| Per-product vertical payload hatch | ✅ Exists (`Product.Metadata`) — but as raw string, not typed |
| Per-order vertical payload hatch | ❌ Missing on `Order` |
| Per-customer vertical payload hatch | ❌ Missing on `Customer` |

The migration to a Chameleon shape will require: (a) extracting `MembershipValidUntil`/`LastPaymentAt` into a new `CustomerMembership` aggregate (with a separate audit ledger), (b) introducing strongly-typed JSON columns via EF Core 9 `ToJson()` / `ComplexProperty` for vertical payloads on `Product`/`OrderItem` (and adding the missing slots on `Order` and `Customer`), and (c) deciding whether `Branch.HasKitchen|HasTables|HasDelivery` should fold into a generic capability matrix.

---

**End of audit.** No code modified.
