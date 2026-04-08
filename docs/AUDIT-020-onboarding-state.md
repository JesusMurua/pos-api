# AUDIT-020 — Onboarding Status State Machine: Structural Audit

**Date:** 2026-04-08
**Scope:** Audit existing Catalog patterns, Business entity, and registration flow to plan a database-backed `OnboardingStatusCatalog` with FK integrity.

---

## 1. Catalog Entity Pattern

### 1.1 Structural Convention (from 11 existing catalogs)

Every catalog in `POS.Domain/Models/Catalogs/` follows this exact skeleton:

```csharp
public class XxxCatalog
{
    public int Id { get; set; }                    // PK: int, auto-increment

    [Required, MaxLength(20)]
    public string Code { get; set; } = null!;      // ALWAYS present, unique index

    [Required, MaxLength(50)]
    public string Name { get; set; } = null!;      // ALWAYS present, localized display name

    // Optional domain-specific fields (varies per catalog):
    // SortOrder (int)          — 7 of 11 catalogs
    // Description (string?)    — DeviceModeCatalog
    // Color (string?)          — KitchenStatusCatalog, DisplayStatusCatalog
    // Level (int)              — UserRoleCatalog
    // HasKitchen, HasTables    — BusinessTypeCatalog
    // PosExperience (string)   — BusinessTypeCatalog
}
```

**Invariants across ALL catalogs:**

| Property | Type | Constraint | Present in |
|----------|------|-----------|------------|
| `Id` | `int` | PK, auto-increment | 11/11 |
| `Code` | `string` | `[Required, MaxLength(20)]`, unique index | 11/11 |
| `Name` | `string` | `[Required, MaxLength(50)]` | 11/11 |

### 1.2 Concrete Examples

**DeviceModeCatalog** (simplest with description):
```csharp
public class DeviceModeCatalog
{
    public int Id { get; set; }
    [Required, MaxLength(20)]  public string Code { get; set; } = null!;
    [Required, MaxLength(50)]  public string Name { get; set; } = null!;
    [MaxLength(200)]           public string? Description { get; set; }
}
```

**KitchenStatusCatalog** (with Color + SortOrder):
```csharp
public class KitchenStatusCatalog
{
    public int Id { get; set; }
    [Required, MaxLength(20)]  public string Code { get; set; } = null!;
    [Required, MaxLength(50)]  public string Name { get; set; } = null!;
    [MaxLength(10)]            public string? Color { get; set; }
    public int SortOrder { get; set; }
}
```

**OrderSyncStatusCatalog** (minimal — Code + Name only):
```csharp
public class OrderSyncStatusCatalog
{
    public int Id { get; set; }
    [Required, MaxLength(20)]  public string Code { get; set; } = null!;
    [Required, MaxLength(50)]  public string Name { get; set; } = null!;
}
```

---

## 2. Fluent Config Pattern

**File:** `POS.Repository/ApplicationDbContext.cs:920-930`

ALL catalogs share the same one-liner fluent config:

```csharp
modelBuilder.Entity<PlanTypeCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
modelBuilder.Entity<BusinessTypeCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
modelBuilder.Entity<ZoneTypeCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });
// ... same pattern for all 11 catalogs
```

**Convention:** Single-line lambda, unique index on `Code`. No additional fluent config unless the catalog has FK relationships (like `BusinessTypeCatalog` which gained a principal key via `BusinessGiro`).

---

## 3. Seed Pattern

**File:** `POS.Repository/DbInitializer.cs:26-141` (`SeedSystemDataAsync`)

Two seeding strategies coexist:

### Strategy A — Guard-and-Insert (10 of 11 catalogs)
```csharp
if (!await context.XxxCatalogs.AnyAsync())
{
    context.XxxCatalogs.AddRange(
        new XxxCatalog { Code = "Value1", Name = "Nombre1", SortOrder = 1 },
        new XxxCatalog { Code = "Value2", Name = "Nombre2", SortOrder = 2 }
    );
    await context.SaveChangesAsync();
}
```
- Only inserts if table is empty. Does NOT update existing rows.
- Used by: `PlanTypeCatalog`, `ZoneTypeCatalog`, `UserRoleCatalog`, `PaymentMethodCatalog`, `KitchenStatusCatalog`, `DisplayStatusCatalog`, `DeviceModeCatalog`, `PromotionTypeCatalog`, `PromotionScopeCatalog`, `OrderSyncStatusCatalog`.

### Strategy B — Upsert (1 of 11 catalogs)
```csharp
private static async Task UpsertBusinessTypeCatalogsAsync(ApplicationDbContext context)
{
    var desired = new List<BusinessTypeCatalog> { ... };
    var existing = await context.BusinessTypeCatalogs.ToListAsync();
    var existingByCode = existing.ToDictionary(e => e.Code);

    foreach (var item in desired)
    {
        if (existingByCode.TryGetValue(item.Code, out var row))
        {
            row.Name = item.Name;
            // ... update all mutable fields
        }
        else
        {
            context.BusinessTypeCatalogs.Add(item);
        }
    }
    await context.SaveChangesAsync();
}
```
- Used only by `BusinessTypeCatalog` because it has domain-specific fields that evolve.

**For `OnboardingStatusCatalog`:** Strategy A (guard-and-insert) is appropriate since onboarding statuses are stable and unlikely to change once deployed.

---

## 4. Business Entity — Current Status Tracking

**File:** `POS.Domain/Models/Business.cs`

### 4.1 Primary Key
```csharp
public int Id { get; set; }   // int, auto-increment
```

### 4.2 Existing Onboarding Fields

| Property | Type | Default | Purpose |
|----------|------|---------|---------|
| `OnboardingCompleted` | `bool` | `false` | Binary flag — only `true` or `false` |

**No step tracking, no status enum, no FK to any catalog.** The current model is a simple boolean with no granularity.

### 4.3 Where `OnboardingCompleted` Is Used

| File | Line(s) | Usage |
|------|---------|-------|
| `AuthService.cs` | 68, 111, 163, 326 | Returned in `AuthResponse.OnboardingCompleted` |
| `AuthService.cs` | 393 | Embedded as JWT claim: `new("onboardingCompleted", ...)` |
| `BusinessController.cs` | 159 | `POST /api/business/complete-onboarding` sets it to `true` |
| `DbInitializer.cs` | 181, 288, 386, 470, 599, 741, 803 | Seed data sets `OnboardingCompleted = true` |
| `IAuthService.cs` | 72 | `AuthResponse` property |

### 4.4 Current CompleteOnboarding Endpoint

**File:** `POS.API/Controllers/BusinessController.cs:153-164`

```csharp
[HttpPost("complete-onboarding")]
[Authorize(Roles = "Owner")]
public async Task<IActionResult> CompleteOnboarding()
{
    var business = await _businessService.GetByIdAsync(BusinessId);
    business.OnboardingCompleted = true;
    await _businessService.UpdateAsync(business);
    var response = await _authService.SwitchBranchAsync(UserId, BranchId);
    return Ok(response);
}
```

Simple toggle — no state validation, no step awareness.

---

## 5. Registration — Business Instantiation Point

**File:** `POS.Services/Service/AuthService.cs:202-212`

```csharp
var business = new Business
{
    Name = request.BusinessName,
    BusinessType = businessType,
    PlanType = planType,
    CountryCode = request.CountryCode ?? "MX",
    TrialEndsAt = trialEndsAt,
    TrialUsed = false,
    IsActive = true,
    CreatedAt = DateTime.UtcNow
    // NOTE: OnboardingCompleted is NOT set — defaults to false (correct)
};
```

`OnboardingCompleted` is omitted (defaults to `false`). The new `OnboardingStatus` FK should be set to `"Pending"` at this exact location.

---

## 6. Required Structure for `OnboardingStatusCatalog`

Based on the project conventions, the new catalog MUST be:

```csharp
// POS.Domain/Models/Catalogs/OnboardingStatusCatalog.cs
using System.ComponentModel.DataAnnotations;

namespace POS.Domain.Models.Catalogs;

public class OnboardingStatusCatalog
{
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Code { get; set; } = null!;

    [Required, MaxLength(50)]
    public string Name { get; set; } = null!;

    public int SortOrder { get; set; }
}
```

**Proposed catalog values:**

| Code | Name | SortOrder | Description |
|------|------|-----------|-------------|
| `Pending` | Pendiente | 1 | Just registered, no steps completed |
| `InProgress` | En progreso | 2 | At least one onboarding step completed |
| `Completed` | Completado | 3 | All onboarding steps done |
| `Skipped` | Omitido | 4 | User explicitly skipped onboarding |

---

## 7. Step-by-Step Implementation Plan

### Step 1 — Create Catalog Entity

- **File:** `POS.Domain/Models/Catalogs/OnboardingStatusCatalog.cs`
- Pattern: `Id` (int) + `Code` (string, max 20) + `Name` (string, max 50) + `SortOrder` (int)
- Matches `PlanTypeCatalog` structure exactly.

### Step 2 — Add DbSet to ApplicationDbContext

- **File:** `POS.Repository/ApplicationDbContext.cs`
- Add: `public DbSet<OnboardingStatusCatalog> OnboardingStatusCatalogs { get; set; } = null!;`
- Add fluent config: `modelBuilder.Entity<OnboardingStatusCatalog>(e => { e.HasIndex(x => x.Code).IsUnique(); });`

### Step 3 — Add FK to Business Entity

- **File:** `POS.Domain/Models/Business.cs`
- Add new property:
  ```csharp
  [Required, MaxLength(20)]
  public string OnboardingStatus { get; set; } = "Pending";
  ```
- Add navigation property:
  ```csharp
  public OnboardingStatusCatalog? OnboardingStatusCatalog { get; set; }
  ```
- **Keep `OnboardingCompleted` temporarily** to avoid breaking JWT/AuthResponse in this migration.

### Step 4 — Configure FK Relationship

- **File:** `POS.Repository/ApplicationDbContext.cs`
- Inside `modelBuilder.Entity<Business>(...)`:
  ```csharp
  entity.HasOne(b => b.OnboardingStatusCatalog)
      .WithMany()
      .HasForeignKey(b => b.OnboardingStatus)
      .HasPrincipalKey(c => c.Code)
      .OnDelete(DeleteBehavior.Restrict);
  ```

### Step 5 — Seed Catalog Data

- **File:** `POS.Repository/DbInitializer.cs` inside `SeedSystemDataAsync`
- Use Strategy A (guard-and-insert):
  ```csharp
  if (!await context.OnboardingStatusCatalogs.AnyAsync())
  {
      context.OnboardingStatusCatalogs.AddRange(
          new OnboardingStatusCatalog { Code = "Pending",    Name = "Pendiente",   SortOrder = 1 },
          new OnboardingStatusCatalog { Code = "InProgress", Name = "En progreso", SortOrder = 2 },
          new OnboardingStatusCatalog { Code = "Completed",  Name = "Completado",  SortOrder = 3 },
          new OnboardingStatusCatalog { Code = "Skipped",    Name = "Omitido",     SortOrder = 4 }
      );
      await context.SaveChangesAsync();
  }
  ```

### Step 6 — Set Default on Registration

- **File:** `POS.Services/Service/AuthService.cs:202-212`
- Add `OnboardingStatus = "Pending"` to the `Business` constructor (explicit, even though it matches the C# default).

### Step 7 — Update CompleteOnboarding Endpoint

- **File:** `POS.API/Controllers/BusinessController.cs:159`
- Change from:
  ```csharp
  business.OnboardingCompleted = true;
  ```
- To:
  ```csharp
  business.OnboardingStatus = "Completed";
  business.OnboardingCompleted = true; // keep for backward compat during transition
  ```

### Step 8 — Generate Migration

```bash
dotnet ef migrations add AddOnboardingStatusCatalog \
    --project POS.Repository --startup-project POS.API
```

The migration should:
1. Create `OnboardingStatusCatalogs` table
2. Add `OnboardingStatus` column to `Businesses` (VARCHAR(20), default `"Pending"`)
3. Add FK constraint to `OnboardingStatusCatalogs.Code`
4. Add unique constraint on `OnboardingStatusCatalogs.Code` (alternate key)

### Step 9 — Data Migration

The migration must backfill existing businesses:
```sql
UPDATE "Businesses"
SET "OnboardingStatus" = CASE
    WHEN "OnboardingCompleted" = true THEN 'Completed'
    ELSE 'Pending'
END;
```

This SQL should be added to the `Up()` method AFTER the catalog seed runs (or handled via `DbInitializer`).

### Step 10 — Build & Verify

```bash
dotnet build   # 0 errors expected
```

---

## 8. Risk Assessment

| Risk | Mitigation |
|------|-----------|
| FK constraint fails if catalog not seeded before business insert | Seed `OnboardingStatusCatalogs` runs in `SeedSystemDataAsync` BEFORE any test data |
| Existing businesses have no `OnboardingStatus` value | Data migration backfills based on `OnboardingCompleted` boolean |
| Frontend reads `onboardingCompleted` from JWT | Keep `bool OnboardingCompleted` and JWT claim during transition |
| `CompleteOnboarding` endpoint sets bool only | Update to set both `OnboardingStatus` and `OnboardingCompleted` |

---

## 9. Files Requiring Changes

| File | Change |
|------|--------|
| `POS.Domain/Models/Catalogs/OnboardingStatusCatalog.cs` | **NEW** |
| `POS.Domain/Models/Business.cs` | Add `OnboardingStatus` + nav property |
| `POS.Repository/ApplicationDbContext.cs` | DbSet + fluent config (unique index + FK) |
| `POS.Repository/DbInitializer.cs` | Seed 4 catalog rows |
| `POS.Services/Service/AuthService.cs` | Set `OnboardingStatus = "Pending"` on registration |
| `POS.API/Controllers/BusinessController.cs` | Set `OnboardingStatus = "Completed"` alongside bool |
| Migration file | **NEW** — create table, add column, backfill data |
