# AUDIT-023 — Customer Order History & Membership Tracking

**Date:** 2026-05-04
**Scope:** Customer ↔ Order linkage, Backoffice "History"/"Total Spent" panel, Membership domain support.
**Trigger:**
1. Admin Backoffice → Customer Details → History is empty and Total Spent = $0.00 even when orders were placed in the POS for that customer.
2. Stakeholder wants to display membership expiration ("vigencia") per customer.
**Status:** Read-only audit. No code changes proposed.

---

## 1. Order ↔ Customer Linkage

### 1.1 The domain model SUPPORTS the link

[Order.cs:106](../POS.Domain/Models/Order.cs#L106):

```csharp
/// <summary>FK to Customer for CRM tracking. Null for anonymous sales.</summary>
public int? CustomerId { get; set; }
...
public virtual Customer? Customer { get; set; }
```

[Customer.cs:78](../POS.Domain/Models/Customer.cs#L78):

```csharp
public virtual ICollection<Order>? Orders { get; set; }
```

EF mapping & index (existing):
- [ApplicationDbContext.cs:525](../POS.Repository/ApplicationDbContext.cs#L525) — `HasForeignKey(o => o.CustomerId)`
- [ApplicationDbContext.cs:550](../POS.Repository/ApplicationDbContext.cs#L550) — `HasIndex(o => o.CustomerId)`

So the database can store `Order.CustomerId` and is indexed for fast customer-scoped queries.

### 1.2 The POS sync DOES accept a CustomerId

[SyncOrderRequest.cs:42](../POS.Domain/Models/SyncOrderRequest.cs#L42):

```csharp
/// <summary>FK to Customer for CRM tracking. Required when payments use StoreCredit or LoyaltyPoints.</summary>
public int? CustomerId { get; set; }
```

It is persisted on both code paths in `OrderService.SyncOrdersAsync`:
- New orders → [OrderService.cs:1361](../POS.Services/Service/OrderService.cs#L1361) (`MapToOrder` copies `request.CustomerId`).
- Existing orders → [OrderService.cs:111](../POS.Services/Service/OrderService.cs#L111) (`existingOrder.CustomerId = request.CustomerId`).

**Implication:** if the POS frontend posts a JSON body with a numeric `customerId`, the backend stores it correctly. If it omits the field (or sends `null`), the order remains anonymous — there is no inference from phone/email/etc.

### 1.3 There is NO endpoint exposing a customer's order history

This is the **root cause** of the empty History panel.

| Surface | Status |
|---------|:------:|
| `GET /api/customers/{id}/orders` | ❌ **Does not exist** |
| `GET /api/customers/{id}/spent` / KPI | ❌ **Does not exist** |
| `GET /api/customers/{id}/transactions` | ✅ Exists — but returns `CustomerTransaction` rows from the credit/loyalty ledger, **not** Orders ([CustomersController.cs:215-222](../POS.API/Controllers/CustomersController.cs#L215-L222)) |
| `GET /api/orders?customerId=...` | ❌ Not supported. The only filter on [`OrdersController.GetByBranchAndDate`](../POS.API/Controllers/OrdersController.cs#L57) is `date`. The `pull` and `by-table` variants don't filter by customer either. |
| `IOrderRepository.GetByCustomerAsync(...)` | ❌ Does not exist (grep returned no matches). |

In [`ICustomerService`](../POS.Services/IService/ICustomerService.cs), the only history-shaped method is `GetTransactionsAsync` (lines 59-61), which queries `CustomerTransactions` (ledger), not `Orders`.

### 1.4 Why the Backoffice shows empty History / $0.00

Two independent causes are possible — both lead to the same symptom:

**Cause A — No backend endpoint to query.**
Even when `Order.CustomerId` is correctly stored, the API has no route that returns a customer's orders. Whatever the Admin frontend is calling either:
- hits `GET /api/customers/{id}/transactions` (credit/loyalty ledger, normally empty for a customer who has only placed regular sales) → renders empty History;
- or aggregates `Customer.Orders` from a generic GET that does not eagerly load `Orders` → collection is `null`, totals compute to 0.

**Cause B — Orders are being saved with `CustomerId = null`.**
If the POS does not include `customerId` in the sync payload (for example, the customer-selection state isn't propagated to the order before sync), `CustomerId` is `null` even when a customer was visually selected at checkout. Indexed FK lookups would then return zero rows.

Both can be true simultaneously. Confirming requires:
1. A direct DB inspection: `SELECT COUNT(*) FROM Orders WHERE CustomerId = <id>` for a recently-tested customer.
2. Inspecting the actual JSON the POS posts to `/api/orders/sync` for the `customerId` property.

---

## 2. Membership Domain Support

### 2.1 Yes, but as a denormalized two-column model — there is NO `CustomerMembership` entity

The repository was searched for any standalone membership entity (`*Membership*.cs`); the only matches are migration files. **No `CustomerMembership` table or class exists.**

Membership is modeled as **two columns directly on `Customer`** ([Customer.cs:59-70](../POS.Domain/Models/Customer.cs#L59-L70)):

```csharp
/// <summary>
/// Membership validity expiration (UTC). Null when the customer has no active membership.
/// Strict column for fast queries (e.g. "memberships expiring this week").
/// Updated by ExtendMembershipAsync when a membership product is sold.
/// </summary>
public DateTime? MembershipValidUntil { get; set; }

/// <summary>
/// Timestamp of the last membership/recurring payment (UTC).
/// Useful for churn analytics and "last seen paying" reports.
/// </summary>
public DateTime? LastPaymentAt { get; set; }
```

A partial filtered index exists for fast "memberships expiring this week" queries: [migration 20260426041125](../POS.Repository/Migrations/20260426041125_AddPartialCustomerMembershipIndex.cs) creates `IX_Customers_MembershipValidUntil` filtered to `MembershipValidUntil IS NOT NULL`.

### 2.2 How a membership gets extended

Service contract: [`ICustomerService.ExtendMembershipAsync`](../POS.Services/IService/ICustomerService.cs#L99-L106):

> Extends a customer's membership validity by `durationDays` days. If the current `MembershipValidUntil` is null or already expired, the new period starts from today (UTC). If the membership is still active, days are stacked on top of the existing expiration to reward early renewals. Always updates `LastPaymentAt` to `DateTime.UtcNow`.

Trigger path during order sync ([OrderService.cs:1542-1626](../POS.Services/Service/OrderService.cs#L1542-L1626)):
- `OrderItem.Metadata` JSON may carry `BeneficiaryCustomerId` (item-level) — falls back to `Order.CustomerId` (order-level payor) when absent.
- For each membership-bearing item, `ExtendMembershipAsync(beneficiaryId, totalDays, order.Id)` is called.
- Reference: [SyncOrderItemRequest.Metadata](../POS.Domain/Models/SyncOrderRequest.cs#L76-L77) — *"Vertical-specific JSON payload (e.g. `{"BeneficiaryCustomerId": 123}` for memberships)"*.

This means the POS must:
1. Send `customerId` on the order, **AND/OR**
2. Send `metadata: { "BeneficiaryCustomerId": <id> }` on the membership line item.
3. The product line must define the duration (days) somewhere — see Product/membership product configuration (out of scope for this audit).

### 2.3 What is NOT exposed for the Backoffice membership UI

| Need | Available? |
|------|:----------:|
| Persist a per-customer expiration date | ✅ `Customer.MembershipValidUntil` |
| Update it when a membership is sold | ✅ `ExtendMembershipAsync` (called during sync) |
| **Read it on the customer profile** | ✅ Implicitly — `GET /api/customers/{id}` returns the full `Customer` entity, which includes `MembershipValidUntil` and `LastPaymentAt` |
| List "memberships expiring soon" | ❌ No dedicated endpoint. Index exists in DB, but no controller action exposes the query. |
| Membership history (renewal log per customer) | ❌ No. The `MembershipValidUntil` column is overwritten in place; only `CustomerTransaction` ledger and the issuing `Order.Id` parameter into `ExtendMembershipAsync` are evidence of the extension. There is no `CustomerMembership` table tracking start/end periods. |
| Pause/cancel/refund a membership | ❌ Not modeled — would require a new entity. |
| Multiple concurrent memberships per customer (e.g. gym + nutrition plan) | ❌ Not supported — the schema is single-tier per customer (`MembershipValidUntil` is one column). |

---

## 3. Findings Summary

### 3.1 Order linkage

1. The schema **fully supports** Order → Customer linkage (`Order.CustomerId`, FK, indexed, navigation properties on both sides).
2. The POS sync DTO **does carry** `CustomerId`, and `OrderService.SyncOrdersAsync` persists it on both insert and update paths.
3. **No API endpoint returns a customer's order history or aggregated spend.** The Customer domain only exposes the credit/loyalty ledger via `GET /api/customers/{id}/transactions`.
4. Empty History / $0.00 in the Admin panel is therefore caused by either (a) the missing endpoint (likely) or (b) the POS not forwarding `customerId` on the sync payload (verifiable). Both must be checked before diagnosing further.

### 3.2 Membership tracking

1. **Yes**, the backend tracks membership validity per customer via two columns on `Customer`:
   - `MembershipValidUntil : DateTime?` ("vigencia")
   - `LastPaymentAt : DateTime?`
2. There is **no `CustomerMembership` entity, no period log, no plan/tier model, no support for multiple parallel memberships, and no "expiring soon" endpoint.**
3. The expiration date is **already exposed** through the standard `GET /api/customers/{id}` payload — the Admin UI can render "vigencia" directly from `customer.membershipValidUntil` once the frontend reads that field.
4. The expiration is updated server-side when a membership product is sold and synced — the trigger is `OrderItem.Metadata.BeneficiaryCustomerId` (or fallback to `Order.CustomerId`) plus a duration-bearing product. POS must send the metadata payload for this to fire.

---

## 4. Recommendations (no code yet — for design discussion)

To unblock the History/Total Spent panel, the gaps to discuss are:

- **A `GET /api/customers/{id}/orders`** (with pagination + date filter) projecting `OrderId`, `OrderNumber`, `CreatedAt`, `TotalCents`, `BranchId`, `BranchName`, `Items` count.
- **A `GET /api/customers/{id}/stats`** (or extend the existing `GetById`) returning `TotalSpentCents`, `OrderCount`, `LastOrderAt`, `AverageTicketCents`.
- **Frontend verification**: confirm the POS posts `customerId` on the sync payload after a customer is selected at checkout — without this, no backend endpoint will help.

For richer membership UX (renewal history, multi-tier plans, expiring-soon alerts) the schema would need to evolve into a real `CustomerMembership` aggregate. The current single-column model only covers the immediate "vigencia" display use case.

---

**End of audit.** No code modified.
