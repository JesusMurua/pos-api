# AUDIT-016: Database Schema & Naming Convention Reality Check

**Date:** 2026-04-07
**Scope:** EF Core naming conventions, `Business` entity fields, registration endpoint payload
**Status:** Final

---

## 1. EF Core Naming Conventions

### 1.1 Table Naming Strategy

**Convention:** EF Core default pluralization via `DbSet<T>` property names. No explicit `.ToTable()` calls anywhere. No `tbl_` prefixes.

| DbSet Declaration | Resulting Table Name |
|-------------------|---------------------|
| `DbSet<Business> Businesses` | `Businesses` |
| `DbSet<Branch> Branches` | `Branches` |
| `DbSet<User> Users` | `Users` |
| `DbSet<Product> Products` | `Products` |
| `DbSet<Order> Orders` | `Orders` |
| `DbSet<OrderItem> OrderItems` | `OrderItems` |
| `DbSet<CashRegister> CashRegisters` | `CashRegisters` |
| `DbSet<CashRegisterSession> CashRegisterSessions` | `CashRegisterSessions` |
| `DbSet<RestaurantTable> RestaurantTables` | `RestaurantTables` |
| `DbSet<Device> Devices` | `Devices` |
| `DbSet<DeviceActivationCode> DeviceActivationCodes` | `DeviceActivationCodes` |
| `DbSet<Invoice> Invoices` | `Invoices` |
| `DbSet<FiscalCustomer> FiscalCustomers` | `FiscalCustomers` |
| `DbSet<BranchDeliveryConfig> BranchDeliveryConfigs` | `BranchDeliveryConfigs` |
| `DbSet<BranchPaymentConfig> BranchPaymentConfigs` | `BranchPaymentConfigs` |
| `DbSet<PlanTypeCatalog> PlanTypeCatalogs` | `PlanTypeCatalogs` |

**Pattern rules observed:**
- Entity name → PascalCase plural as DbSet property name → becomes table name.
- Compound names use PascalCase concatenation: `CashRegisterSession`, `BranchDeliveryConfig`.
- Catalogs follow `{Domain}Catalog` → `{Domain}Catalogs` pattern.
- No snake_case, no `tbl_` prefixes, no schema separation.

**Two naming anomalies found:**

| DbSet | Expected | Actual |
|-------|----------|--------|
| `DbSet<StripeEventInbox> StripeEventInbox` | `StripeEventInboxes` | `StripeEventInbox` (singular) |
| `DbSet<PaymentWebhookInbox> PaymentWebhookInbox` | `PaymentWebhookInboxes` | `PaymentWebhookInbox` (singular) |

These two tables break the plural convention. Not blocking, but should be noted for consistency.

### 1.2 Column Naming

- EF Core convention: property name → column name (PascalCase in PostgreSQL).
- Enum properties stored as strings via `HasConversion<string>()` with `MaxLength(20)`.
- No `[Column("...")]` overrides found.

### 1.3 Index Naming

EF Core auto-generates index names:
- Single column: `IX_{Table}_{Column}` — e.g., `IX_Users_Email`
- Composite: `IX_{Table}_{Col1}_{Col2}` — e.g., `IX_Products_BranchId_Barcode`
- Unique indexes use same pattern with `.IsUnique()`.
- Filtered indexes use `.HasFilter("\"Column\" IS NOT NULL")` (PostgreSQL double-quote syntax).

---

## 2. Business Entity — Current Fields

**File:** `POS.Domain/Models/Business.cs` (72 lines)

```
FIELD                       TYPE              DEFAULT           NOTES
─────────────────────────────────────────────────────────────────────────
Id                          int               auto              PK
Name                        string [100]      required          Business display name
BusinessType                BusinessType      General           Enum → string [20]
PlanType                    PlanType          Free              Enum → string [20]
TrialEndsAt                 DateTime?         null              Set to +14 days for paid plans
TrialUsed                   bool              false
OnboardingCompleted         bool              false
IsActive                    bool              true
CreatedAt                   DateTime          UtcNow

── Fiscal / Invoicing ──
Rfc                         string? [13]      null              SAT tax ID
TaxRegime                   string? [3]       null              SAT regime code ("601", "612")
LegalName                   string? [300]     null              Razon social
InvoicingEnabled            bool              false
FacturapiOrganizationId     string? [50]      null              Facturapi org ID

── Loyalty Program ──
LoyaltyEnabled              bool              false
PointsPerCurrencyUnit       int               1
CurrencyUnitsPerPoint       int               1000              ($10 MXN)
PointRedemptionValueCents   int               10                ($0.10 MXN)

── Navigation ──
Branches                    ICollection<Branch>?
Users                       ICollection<User>?
Subscription                Subscription?
```

### Fields That Do NOT Exist

| Field | Status | Where It's Hard-Coded Instead |
|-------|--------|-------------------------------|
| `Country` | **Missing** | Entire system assumes Mexico |
| `CountryCode` | **Missing** | No reference anywhere |
| `Currency` | **Missing** | Hard-coded `"MXN"` in `Invoice.cs:52` |
| `Timezone` | **Missing** | Read from `X-Timezone` HTTP header per request; fallback `"America/Mexico_City"` in `TimeZoneHelper.cs:8` |
| `Locale` | **Missing** | No i18n at Business level |
| `DefaultTaxRate` | **Missing** | Hard-coded `0.16m` in `OrderService.cs` and `InvoicingService.cs` |

---

## 3. Registration Endpoint — Exact Payload

### 3.1 API Request DTO

**Endpoint:** `POST /api/auth/register`
**Rate limited:** `RegistrationPolicy`
**Auth:** Public (no JWT required)

**`RegisterApiRequest`** (defined in `POS.API/Models/AuthRequests.cs:39-60`):

```csharp
{
    "businessName":  string,   // Required, max 100
    "ownerName":     string,   // Required, max 100
    "email":         string,   // Required, valid email
    "password":      string,   // Required, min 8 chars
    "businessType":  string?,  // Optional ("Restaurant", "Cafe", etc.)
    "planType":      string?   // Optional ("Free", "Basic", "Pro")
}
```

### 3.2 Service Layer DTO

**`RegisterRequest`** (defined in `POS.Services/IService/IAuthService.cs:35-43`):

```csharp
{
    BusinessName, OwnerName, Email, Password, BusinessType?, PlanType?
}
```

1:1 mapping from `RegisterApiRequest` — no transformation, no additional fields.

### 3.3 Defaults Applied in `AuthService.RegisterAsync()`

| Field | Source | Default if missing |
|-------|--------|--------------------|
| `Business.Name` | `request.BusinessName` | — (required) |
| `Business.BusinessType` | `Enum.TryParse(request.BusinessType)` | `BusinessType.General` |
| `Business.PlanType` | `Enum.TryParse(request.PlanType)` | `PlanType.Free` |
| `Business.TrialEndsAt` | Computed | `null` (Free) or `UtcNow + 14 days` (paid) |
| `Business.TrialUsed` | Hard-coded | `false` |
| `Business.IsActive` | Hard-coded | `true` |
| `Branch.Name` | Computed | `"{BusinessName} Principal"` |
| `Branch.IsMatrix` | Hard-coded | `true` |
| `Branch.HasKitchen` | Derived from BusinessType | `true` for Restaurant/Cafe/Bar/FoodTruck |
| `Branch.HasTables` | Derived from BusinessType | `true` for Restaurant/Cafe/Bar |
| `User.Role` | Hard-coded | `UserRole.Owner` |

### 3.4 Entities Created During Registration (Single Transaction)

1. `Business` — with defaults above
2. `Branch` — matrix branch, named "{BusinessName} Principal"
3. `User` — Owner role, email + hashed password
4. `UserBranch` — links User ↔ Branch, `IsDefault = true`
5. `Category` — "General" default category (icon: `pi-tag`)
6. `Zone[]` — default zones based on BusinessType (e.g., "Salón", "Barra" for Restaurant)
7. `RestaurantTable` — "Mesa 1" if `hasTables` is true

### 3.5 Data Gaps in Registration

| Missing Field | Impact | Where It Should Go |
|---------------|--------|--------------------|
| `country` | Cannot determine tax rules, currency, locale, fiscal system | `Business.Country` or `Business.CountryCode` |
| `currency` | Hard-coded MXN — blocks international expansion | `Business.Currency` (derived from country) |
| `timezone` | Relies on HTTP header — lost if header missing | `Business.Timezone` (derived from country, overridable) |
| `defaultTaxRate` | Hard-coded 16% — wrong for border zone or other countries | `Business.DefaultTaxRate` (derived from country) |
| `fiscalZipCode` | Not collected at registration — required later for CFDI | `Branch.FiscalZipCode` (could prompt during onboarding) |

---

## 4. Existing Catalog Entities

**Location:** `POS.Domain/Models/Catalogs/` — 11 catalog entities

| Catalog | Key Fields | Seeded Values | Purpose |
|---------|-----------|---------------|---------|
| `PlanTypeCatalog` | Code, Name, SortOrder | Free, Basic, Pro, Enterprise | Subscription tiers |
| `BusinessTypeCatalog` | Code, Name, HasKitchen, HasTables, PosExperience, SortOrder | Restaurant, Cafe, Bar, FoodTruck, Retail, General | Business vertical templates |
| `ZoneTypeCatalog` | Code, Name, SortOrder | Salon, BarSeats, Other | Physical zone classification |
| `UserRoleCatalog` | Code, Name, Level | Owner, Manager, Cashier, Kitchen, Waiter, Kiosk, Host | User role metadata |
| `PaymentMethodCatalog` | Code, Name, SortOrder | Cash, Card, Transfer, etc. | Payment method reference |
| `KitchenStatusCatalog` | Code, Name, Color, SortOrder | Pending, Cooking, Ready, etc. | KDS order states |
| `DisplayStatusCatalog` | Code, Name, Color, SortOrder | KDS display screen states |  |
| `DeviceModeCatalog` | Code, Name, Description | cashier, kiosk, tables, kitchen | Device operational modes |
| `PromotionTypeCatalog` | Code, Name, SortOrder | Discount types | Promotion mechanisms |
| `PromotionScopeCatalog` | Code, Name | Category, Product, Order | Promotion targets |
| `OrderSyncStatusCatalog` | Code, Name | Delivery platform sync states |  |

**Common catalog pattern:**
- `Id` (int PK), `Code` (string, unique index, max 20), `Name` (string, max 50)
- Optional: `SortOrder`, `Color`, `Description`, domain-specific booleans
- All seeded in `DbInitializer.cs`

**No `CountryCatalog` or `CurrencyCatalog` or `TaxRateCatalog` exists.**

---

## 5. Recommendation: Minimal Change to Accept `country=MX`

To accept the landing page's `country` parameter without a large refactor:

1. **Add `CountryCode` to `Business`:** `string [2]`, default `"MX"`, ISO 3166-1 alpha-2.
2. **Add `CountryCode` to `RegisterApiRequest` and `RegisterRequest`:** optional, default `"MX"`.
3. **Set during `RegisterAsync()`:** `business.CountryCode = request.CountryCode ?? "MX"`.
4. **Migration:** `AddBusinessCountryCode`.
5. **No catalog needed yet** — a `CountryCatalog` is premature until multi-country support is scoped. A simple string column with a constants class is sufficient for now.

Future phases would derive `Currency`, `Timezone`, and `DefaultTaxRate` from `CountryCode` using a helper/lookup, but that is out of scope for the immediate fix.
