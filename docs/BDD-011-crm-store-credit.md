# BDD-011 — CRM, Store Credit (Fiado) & Loyalty: Status & Completion Plan
**Fase:** 16 | **Estado:** Parcialmente implementado | **Fecha:** 2026-04-05
**Documento base:** [BDD-005-crm-loyalty.md](BDD-005-crm-loyalty.md)

---

## 1. Current State vs Proposed State

### 1.1 What's ALREADY IMPLEMENTED (Subfases 16a–16c partial)

BDD-005 was the original design. The following has been built and is functional:

| Component | Status | Files |
|---|---|---|
| `Customer` entity (full model) | DONE | `POS.Domain/Models/Customer.cs` |
| `CustomerTransaction` entity (ledger) | DONE | `POS.Domain/Models/CustomerTransaction.cs` |
| `CustomerTransactionType` enum | DONE | `POS.Domain/Enums/CustomerTransactionType.cs` |
| `Order.CustomerId` nullable FK | DONE | `POS.Domain/Models/Order.cs` |
| `Reservation.CustomerId` nullable FK | DONE | `POS.Domain/Models/Reservation.cs` |
| `FiscalCustomer.CustomerId` nullable FK | DONE | `POS.Domain/Models/FiscalCustomer.cs` |
| `PaymentMethod.StoreCredit` (7) | DONE | `POS.Domain/Enums/PaymentMethod.cs` |
| `PaymentMethod.LoyaltyPoints` (8) | DONE | `POS.Domain/Enums/PaymentMethod.cs` |
| `ICustomerRepository` + impl | DONE | `POS.Repository/IRepository/ICustomerRepository.cs` |
| `ICustomerTransactionRepository` + impl | DONE | `POS.Repository/IRepository/ICustomerTransactionRepository.cs` |
| `IUnitOfWork.Customers` + `CustomerTransactions` | DONE | `POS.Repository/IUnitOfWork.cs` |
| `ICustomerService` (CRUD + manual adjustments) | DONE | `POS.Services/IService/ICustomerService.cs` |
| `CustomerService` implementation | DONE | `POS.Services/Service/CustomerService.cs` |
| `CustomersController` (full REST API) | DONE | `POS.API/Controllers/CustomersController.cs` |
| EF migration for all schema changes | DONE | Applied |

### 1.2 Current `ICustomerService` Methods (Already Implemented)

```
Task<Customer?> GetByIdAsync(int id)
Task<IEnumerable<Customer>> GetByBusinessAsync(int businessId)
Task<IEnumerable<Customer>> SearchAsync(int businessId, string query)
Task<Customer> CreateAsync(int businessId, Customer customer)
Task<Customer> UpdateAsync(int id, Customer customer)
Task DeactivateAsync(int id)
Task<CustomerTransaction> AddCreditAsync(int customerId, int amountCents, string description, int branchId, string createdBy)
Task<CustomerTransaction> AdjustCreditAsync(int customerId, int amountCents, string description, int branchId, string createdBy)
Task<CustomerTransaction> AdjustPointsAsync(int customerId, int points, string description, int branchId, string createdBy)
Task<IEnumerable<CustomerTransaction>> GetTransactionsAsync(int customerId, DateTime? from, DateTime? to)
```

### 1.3 Current `CustomerTransaction` Entity (Already Implemented)

```
Id: int (PK)
CustomerId: int (FK)
BranchId: int (FK)
TransactionType: CustomerTransactionType (enum)
AmountCents: int
PointsAmount: int
BalanceAfterCents: int
PointsBalanceAfter: int
ReferenceOrderId: string? (FK to Order)
Description: string (200)
CreatedBy: string (100)
CreatedAt: DateTime
```

### 1.4 What's MISSING (Gaps)

| Gap | BDD-005 Section | Severity | Description |
|---|---|---|---|
| **G-1** | 5.1 `UseCreditAsync` | **CRITICAL** | No method to consume credit (fiado) from an order payment. Manual `AddCreditAsync` exists (customer pays down tab), but there's no `UseCreditAsync` (customer charges to tab). |
| **G-2** | 5.1 `EarnPointsAsync` | **HIGH** | No method to auto-calculate and award points based on order total × business loyalty config. |
| **G-3** | 5.1 `RedeemPointsAsync` | **HIGH** | No method to redeem points as payment (convert points → cents at configured rate). |
| **G-4** | 7.1 Sync Engine integration | **CRITICAL** | `OrderService.SyncOrdersAsync` does NOT validate or process `StoreCredit`/`LoyaltyPoints` payment methods. Orders with these methods would be persisted but no balance deduction or point earn/redeem occurs. |
| **G-5** | 5.1 `RecalculateBalancesAsync` | MEDIUM | No reconciliation method to recalculate denormalized balances from ledger. |
| **G-6** | 5.1 `LinkFiscalCustomerAsync` | LOW | No endpoint to link a CRM Customer to an existing FiscalCustomer. |
| **G-7** | 4.7 Business loyalty config | MEDIUM | Need to verify Business model has `LoyaltyEnabled`, `PointsPerCurrencyUnit`, `CurrencyUnitsPerPoint`, `PointRedemptionValueCents` fields. |

---

## 2. Database Schema — No Changes Needed

All entities and relationships are already in place:

```
Business (1) ──────── (*) Customer
                            │
                            ├── (1) ──── (*) CustomerTransaction  [Ledger]
                            ├── (1) ──── (*) Order.CustomerId      [FK]
                            ├── (1) ──── (*) Reservation.CustomerId [FK]
                            └── (1) ──── (0..1) FiscalCustomer.CustomerId [FK]
```

**No new migrations required.** The schema is complete per BDD-005 sections 9.1–9.6.

---

## 3. Files to Modify (Gaps Only)

| File | Action | Gap |
|---|---|---|
| `POS.Services/IService/ICustomerService.cs` | **Modify** | Add `UseCreditAsync`, `EarnPointsAsync`, `RedeemPointsAsync`, `RecalculateBalancesAsync`, `LinkFiscalCustomerAsync` |
| `POS.Services/Service/CustomerService.cs` | **Modify** | Implement the 5 new methods with transactional safety |
| `POS.Services/Service/OrderService.cs` | **Modify** | Add Phase 2c (validate StoreCredit/Points) + Phase 5c (process transactions) to `SyncOrdersAsync` |
| `POS.API/Controllers/CustomersController.cs` | **Modify** | Add `LinkFiscalCustomer` endpoint |
| `POS.Domain/Models/Business.cs` | **Verify** | Confirm loyalty config fields exist; add if missing |

**No new files needed.** All models, repositories, controllers, and DI registrations already exist.

---

## 4. New Method Signatures for ICustomerService

### 4.1 `UseCreditAsync` — Consume credit (fiado) from an order

```
/// Consumes store credit when a customer charges an order to their tab.
/// Creates a CustomerTransaction of type UseCredit.
/// Validates: IsActive, CreditLimitCents not exceeded.
/// Must run inside a DB transaction (caller's responsibility for Sync Engine).
Task<CustomerTransaction> UseCreditAsync(
    int customerId,
    int amountCents,
    string orderId,
    int branchId,
    string createdBy)
```

**Business rules:**
- `amountCents` must be > 0
- `Customer.IsActive` must be `true`
- If `Customer.CreditLimitCents > 0`: `abs(Customer.CreditBalanceCents - amountCents)` must be `<= CreditLimitCents`
- Creates `CustomerTransaction` with `TransactionType = UseCredit`, `AmountCents = -amountCents` (negative = consumed)
- Updates `Customer.CreditBalanceCents -= amountCents`
- Snapshot: `BalanceAfterCents = Customer.CreditBalanceCents` after update

### 4.2 `EarnPointsAsync` — Award points for a completed order

```
/// Calculates and awards loyalty points based on order total and business config.
/// Creates a CustomerTransaction of type EarnPoints.
/// Only processes if Business.LoyaltyEnabled == true.
Task<CustomerTransaction?> EarnPointsAsync(
    int customerId,
    int orderTotalCents,
    string orderId,
    int branchId,
    string createdBy)
```

**Business rules:**
- Returns `null` if `Business.LoyaltyEnabled == false` (no-op)
- Points calculation: `orderTotalCents / Business.CurrencyUnitsPerPoint * Business.PointsPerCurrencyUnit`
- Example: order $150 MXN (15000 cents), config 1 point per 1000 cents → 15 points
- Creates `CustomerTransaction` with `TransactionType = EarnPoints`, `PointsAmount = +earnedPoints`
- Updates `Customer.PointsBalance += earnedPoints`
- Snapshot: `PointsBalanceAfter = Customer.PointsBalance` after update

### 4.3 `RedeemPointsAsync` — Convert points to payment

```
/// Redeems loyalty points as payment for an order.
/// Creates a CustomerTransaction of type RedeemPoints.
/// Returns the cent value of the redeemed points (for OrderPayment.AmountCents).
Task<(CustomerTransaction Transaction, int RedeemedValueCents)> RedeemPointsAsync(
    int customerId,
    int points,
    string orderId,
    int branchId,
    string createdBy)
```

**Business rules:**
- `Customer.PointsBalance >= points` (throws `ValidationException` if insufficient)
- `Business.LoyaltyEnabled == true` (throws `ValidationException` if disabled)
- Cent value: `points * Business.PointRedemptionValueCents`
- Example: 100 points × 10 cents/point = 1000 cents ($10 MXN)
- Creates `CustomerTransaction` with `TransactionType = RedeemPoints`, `PointsAmount = -points`
- Updates `Customer.PointsBalance -= points`

### 4.4 `RecalculateBalancesAsync` — Ledger reconciliation

```
/// Recalculates denormalized balances from the transaction ledger.
/// Used for reconciliation / data integrity checks.
Task RecalculateBalancesAsync(int customerId)
```

**Logic:**
- `CreditBalanceCents = SUM(AmountCents) FROM CustomerTransaction WHERE CustomerId = X`
- `PointsBalance = SUM(PointsAmount) FROM CustomerTransaction WHERE CustomerId = X`
- Updates Customer entity with recalculated values

### 4.5 `LinkFiscalCustomerAsync` — Link CRM to fiscal data

```
/// Links a CRM Customer to an existing FiscalCustomer for invoice generation.
/// Validates both entities belong to the same Business.
Task LinkFiscalCustomerAsync(int customerId, int fiscalCustomerId)
```

---

## 5. Transactional Safety Strategy — Store Credit Ledger

### 5.1 The Race Condition Problem

Two POS terminals (BranchA, BranchB) process StoreCredit payments for the same Customer simultaneously:

```
Terminal A reads: CreditBalanceCents = -500 (owes $5)
Terminal B reads: CreditBalanceCents = -500 (owes $5)
Terminal A writes: CreditBalanceCents = -500 - 300 = -800   ← charges $3
Terminal B writes: CreditBalanceCents = -500 - 200 = -700   ← charges $2
                                                              LOST UPDATE!
Expected: -500 - 300 - 200 = -1000
Actual:   -700 (Terminal B overwrote Terminal A)
```

### 5.2 Solution: Pessimistic Locking via `SELECT ... FOR UPDATE`

All balance-mutating methods (`UseCreditAsync`, `EarnPointsAsync`, `RedeemPointsAsync`, `AddCreditAsync`, `AdjustCreditAsync`, `AdjustPointsAsync`) must follow this pattern:

```
Step 1: BEGIN TRANSACTION (via _unitOfWork.BeginTransactionAsync)
Step 2: SELECT Customer WHERE Id = X  → with row-level lock
        (EF Core: use raw SQL or IsolationLevel.Serializable on the transaction)
Step 3: Validate business rules against locked row
Step 4: UPDATE Customer balance fields
Step 5: INSERT CustomerTransaction ledger entry (with BalanceAfter snapshot)
Step 6: COMMIT TRANSACTION
```

**Implementation approach for EF Core + PostgreSQL/SQL Server:**

```
// Option A — Optimistic concurrency (EF Core built-in)
// Add [ConcurrencyCheck] or RowVersion to Customer entity.
// If two transactions read the same row, the second SaveChanges
// throws DbUpdateConcurrencyException → retry.

// Option B — Explicit transaction with elevated isolation
await using var tx = await _unitOfWork.BeginTransactionAsync();
// EF Core will hold the row lock until commit
var customer = await _unitOfWork.Customers.GetByIdAsync(customerId);
// ... validate, mutate, add transaction ...
await _unitOfWork.SaveChangesAsync();
await tx.CommitAsync();
```

**Recommendation:** Option A (optimistic concurrency with `RowVersion`) is simpler and performs better for POS workloads where contention is rare (same customer paying at two terminals simultaneously is uncommon). Option B is safer for high-volume tenants.

### 5.3 Sync Engine Integration Pattern

Within `OrderService.SyncOrdersAsync`, StoreCredit/LoyaltyPoints payments must be processed **inside the existing transaction**:

```
SyncOrdersAsync existing flow:
  Phase 1: Parse & validate
  Phase 2: Classify orders (new/update)
  ──────────────────────────────────────────
  Phase 2c (NEW): Validate customer payments
    For each order with StoreCredit/LoyaltyPoints payments:
    - Load Customer (fail-fast if not found or inactive)
    - Validate credit limit / points balance
    - If invalid → mark order as sync error, continue
  ──────────────────────────────────────────
  Phase 3: Persist orders (SaveChangesAsync)
  Phase 4: Generate print jobs
  Phase 5: Process promotions
  ──────────────────────────────────────────
  Phase 5c (NEW): Process customer transactions
    For each successfully persisted order with customer payments:
    - StoreCredit → call UseCreditAsync (deduct balance, create ledger entry)
    - LoyaltyPoints → call RedeemPointsAsync (deduct points, create ledger entry)
    - Any order with CustomerId + LoyaltyEnabled → call EarnPointsAsync
    SaveChangesAsync (within same transaction)
  ──────────────────────────────────────────
  Phase 6: Return sync results
```

**Key constraint:** Phase 5c runs after Phase 3 succeeds — the Order is already persisted with its `CustomerId` and `OrderPayment` records. If Phase 5c fails for a specific order (e.g., insufficient credit due to a race), the order is still saved but the payment status should be flagged.

### 5.4 Invariant: Ledger is Source of Truth

`Customer.CreditBalanceCents` and `Customer.PointsBalance` are **denormalized caches**. `CustomerTransaction` is the source of truth. If there's ever a discrepancy, `RecalculateBalancesAsync` recalculates from the ledger.

Every `CustomerTransaction` entry includes `BalanceAfterCents` and `PointsBalanceAfter` as snapshots, enabling:
- Point-in-time balance queries
- Audit trail
- Dispute resolution

---

## 6. Implementation Order

| Step | Description | Depends on |
|---|---|---|
| **1** | Verify `Business` model has loyalty config fields | — |
| **2** | Add `UseCreditAsync`, `EarnPointsAsync`, `RedeemPointsAsync` to `ICustomerService` + implement | Step 1 |
| **3** | Add `RecalculateBalancesAsync` + `LinkFiscalCustomerAsync` to `ICustomerService` + implement | Step 2 |
| **4** | Add `LinkFiscalCustomer` endpoint to `CustomersController` | Step 3 |
| **5** | Integrate Phase 2c + 5c into `OrderService.SyncOrdersAsync` | Step 2 |
| **6** | Test end-to-end: create customer → place order with StoreCredit → verify balance deducted | Step 5 |

---

## 7. Backward Compatibility

- All existing customers, transactions, and orders remain unchanged.
- `SyncOrdersAsync` continues to work for non-customer orders (CustomerId = null).
- Existing payment methods (Cash, Card, etc.) are unaffected.
- `StoreCredit` and `LoyaltyPoints` payment methods require `CustomerId` on the order — if missing, the Sync Engine rejects with a descriptive error.
- `Business.LoyaltyEnabled = false` (default) means point operations are no-ops.

---

## 8. What's NOT in Scope

Per BDD-005 Section 13 — unchanged:
- Mobile loyalty app for customers
- Physical loyalty cards / customer QR codes
- Marketing campaigns (email/SMS)
- Customer duplicate merge
- Bulk import from Excel
- Aging report (accounts receivable)
- Push notifications for credit limit alerts
- Cross-business point conversion
