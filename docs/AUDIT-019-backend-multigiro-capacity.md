# AUDIT-019 — Multi-Giro Capacity Analysis

**Date:** 2026-04-07
**Scope:** Evaluate the current `BusinessType` model and plan the transition to multi-giro (multiple business categories per tenant).

---

## 1. Current State

### 1.1 BusinessType Enum

**File:** `POS.Domain/Enums/BusinessType.cs`

```csharp
public enum BusinessType
{
    Restaurant, Retail, Cafe, Bar, FoodTruck, General,
    Taqueria, Abarrotes, Ferreteria, Papeleria, Farmacia, Servicios
}
```

12 values. No `[Flags]` attribute. Used as a **single scalar value** throughout the codebase.

### 1.2 Business Entity

**File:** `POS.Domain/Models/Business.cs:14`

```csharp
public BusinessType BusinessType { get; set; } = BusinessType.General;
```

Single enum property — one giro per business.

### 1.3 Persistence

**File:** `POS.Repository/ApplicationDbContext.cs` (fluent config)

```csharp
entity.Property(b => b.BusinessType)
    .HasConversion<string>()
    .HasMaxLength(20);
```

Stored as `VARCHAR(20)` (string conversion). Column default: `"General"`.
Migration: `20260328072104_AddPlanGiroZoneFolio.cs`.

### 1.4 BusinessTypeCatalog (UI Catalog)

**File:** `POS.Domain/Models/Catalogs/BusinessTypeCatalog.cs`

| Column | Type | Purpose |
|--------|------|---------|
| `Code` | string(20), unique | Maps 1:1 to enum name |
| `Name` | string(50) | Localized display name |
| `HasKitchen` | bool | Determines branch kitchen feature |
| `HasTables` | bool | Determines branch table service |
| `PosExperience` | string(20) | UI experience key |
| `SortOrder` | int | Display ordering |

Seeded with 12 records in `DbInitializer.UpsertBusinessTypeCatalogsAsync()`.

---

## 2. BusinessType Dependency Map

### 2.1 AuthService.cs — Registration (CRITICAL PATH)

**File:** `POS.Services/Service/AuthService.cs:179-236`

| Line(s) | Usage | Impact Level |
|---------|-------|-------------|
| 179-180 | `Enum.TryParse<BusinessType>(request.BusinessType)` — parses single string | 🔴 Must change to accept array |
| 189 | `hasKitchen = businessType is Restaurant or Cafe or Bar or FoodTruck` | 🔴 Must aggregate across N giros |
| 190 | `hasTables = businessType is Restaurant or Cafe or Bar` | 🔴 Must aggregate across N giros |
| 236 | `BuildDefaultZones(branch, businessType)` — single value switch | 🟡 Needs multi-giro zone strategy |

### 2.2 AuthService.cs — JWT Token Generation (CRITICAL PATH)

**File:** `POS.Services/Service/AuthService.cs:369`

```csharp
new("businessType", business.BusinessType.ToString())
```

Emitted in three flows: `EmailLoginAsync`, `PinLoginAsync`, `SwitchBranchAsync`.

| Impact | Detail |
|--------|--------|
| 🔴 | JWT claim currently carries a single string. Frontend parses this for feature flags. Must become an array claim or comma-separated list. |

### 2.3 AuthService.cs — BuildDefaultZones (MODERATE)

**File:** `POS.Services/Service/AuthService.cs:399-415`

Logic: `Retail/FoodTruck/General → no zones`, `Bar → Salón+Barra+Terraza`, `else → Salón+Terraza`.

With multi-giro, zone defaults must be the **union** of all selected giros' configurations.

### 2.4 BusinessController.cs — UpdateType Endpoint

**File:** `POS.API/Controllers/BusinessController.cs:77-90`

```csharp
[HttpPut("type")]
public async Task<IActionResult> UpdateType([FromBody] UpdateBusinessTypeRequest request)
```

Accepts a single `BusinessType` string. Must be redesigned for array input.

### 2.5 BusinessService.cs — No Direct Reference

`BusinessService` does not reference `BusinessType` directly. Low impact.

### 2.6 Seed Data

`DbInitializer` seeds test businesses each with a single `BusinessType`. Must be updated for multi-giro test scenarios.

---

## 3. Architectural Options Evaluated

### Option A: `[Flags]` Bitwise Enum

```csharp
[Flags]
public enum BusinessType
{
    Restaurant  = 1 << 0,
    Retail      = 1 << 1,
    Cafe        = 1 << 2,
    // ...
}
```

| Pro | Con |
|-----|-----|
| Zero new tables | Breaks all existing string values in DB |
| Fast bitwise checks | Max 32/64 giros (hard ceiling) |
| Single column | Cannot attach metadata (HasKitchen, PosExperience) per giro |
| | Query filtering is awkward (`HasFlag` doesn't translate to clean SQL) |
| | Frontend must understand bit arithmetic |

**Verdict:** ❌ Too fragile, loses catalog metadata, bad DX.

### Option B: JSON Array Column

```csharp
// On Business entity
public List<string> BusinessTypes { get; set; } = ["General"];
```

EF Core config:
```csharp
entity.Property(b => b.BusinessTypes)
    .HasColumnType("nvarchar(500)")
    .HasConversion(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null)!);
```

| Pro | Con |
|-----|-----|
| No new tables, single migration | No FK integrity — orphan strings possible |
| Simple reads/writes | Cannot join/filter efficiently in SQL |
| Works with existing `BusinessTypeCatalog` | No cascade delete if a catalog entry is removed |
| Easy frontend serialization | Violates 1NF |

**Verdict:** 🟡 Acceptable for MVP, but accumulates debt.

### Option C: Many-to-Many Junction Table ✅ RECOMMENDED

```
Businesses ──1:N──> BusinessGiros <──N:1── BusinessTypeCatalogs
```

New entity:
```csharp
public class BusinessGiro
{
    public int BusinessId { get; set; }
    public int BusinessTypeCatalogId { get; set; }

    public Business Business { get; set; } = null!;
    public BusinessTypeCatalog BusinessTypeCatalog { get; set; } = null!;
}
```

EF Core config:
```csharp
modelBuilder.Entity<BusinessGiro>(entity =>
{
    entity.HasKey(bg => new { bg.BusinessId, bg.BusinessTypeCatalogId });

    entity.HasOne(bg => bg.Business)
        .WithMany(b => b.BusinessGiros)
        .HasForeignKey(bg => bg.BusinessId)
        .OnDelete(DeleteBehavior.Cascade);

    entity.HasOne(bg => bg.BusinessTypeCatalog)
        .WithMany()
        .HasForeignKey(bg => bg.BusinessTypeCatalogId)
        .OnDelete(DeleteBehavior.Restrict);
});
```

| Pro | Con |
|-----|-----|
| Full referential integrity | One new table + migration |
| Efficient SQL joins & filtering | Slightly more complex queries |
| Leverages existing `BusinessTypeCatalog` | Requires data migration for existing rows |
| No artificial limit on giro count | |
| Metadata (HasKitchen, HasTables, PosExperience) comes from FK join | |
| Standard relational pattern — zero surprises | |

**Verdict:** ✅ Cleanest long-term solution. Minimal debt.

---

## 4. Recommended Implementation Plan

### Phase 1 — Schema Migration (Non-Breaking)

1. **Create `BusinessGiro` entity** in `POS.Domain/Models/`
2. **Add navigation** to `Business.cs`:
   ```csharp
   public virtual ICollection<BusinessGiro> BusinessGiros { get; set; } = [];
   ```
3. **Add EF config** with composite PK `(BusinessId, BusinessTypeCatalogId)`
4. **Generate migration** that:
   - Creates `BusinessGiros` table
   - Runs SQL data migration to copy each `Business.BusinessType` string into a `BusinessGiro` row (matching `BusinessTypeCatalog.Code`)
5. **Keep `Business.BusinessType` column** temporarily (read-only, deprecated) for backward compatibility

### Phase 2 — Service Layer Refactor

6. **Create helper method** in `BusinessService` or a shared utility:
   ```csharp
   public static (bool HasKitchen, bool HasTables) AggregateFeatures(
       IEnumerable<BusinessTypeCatalog> giros)
   {
       return (
           giros.Any(g => g.HasKitchen),
           giros.Any(g => g.HasTables)
       );
   }
   ```
7. **Refactor `AuthService.RegisterAsync`:**
   - Accept `string[] BusinessTypes` (array) in `RegisterRequest`
   - Resolve each string to `BusinessTypeCatalog` via lookup
   - Create `BusinessGiro` rows per catalog match
   - Compute `hasKitchen`/`hasTables` via `AggregateFeatures()`
   - Compute default zones as **union** of all selected giros' zone rules
8. **Refactor `BuildDefaultZones`** to accept `IEnumerable<BusinessTypeCatalog>` instead of single enum

### Phase 3 — API & JWT

9. **Update `PUT /api/business/type`** → `PUT /api/business/giros`
   - Accept `{ "giros": ["Restaurant", "Abarrotes"] }`
   - Replace all `BusinessGiro` rows atomically
   - Recompute branch features (HasKitchen, HasTables) if needed
10. **Update JWT claim:**
    ```csharp
    new("businessTypes", JsonSerializer.Serialize(
        business.BusinessGiros.Select(bg => bg.BusinessTypeCatalog.Code)))
    ```
    Frontend must parse as JSON array instead of single string.

### Phase 4 — Cleanup

11. **Drop `Business.BusinessType` column** once frontend is fully migrated
12. **Remove `BusinessType` enum** (all logic now driven by catalog FK relationships)
13. **Update seed data** to create `BusinessGiro` rows instead of setting enum property

---

## 5. Data Migration SQL (Phase 1)

```sql
-- Populate BusinessGiros from existing BusinessType column
INSERT INTO BusinessGiros (BusinessId, BusinessTypeCatalogId)
SELECT b.Id, c.Id
FROM Businesses b
JOIN BusinessTypeCatalogs c ON c.Code = b.BusinessType
WHERE NOT EXISTS (
    SELECT 1 FROM BusinessGiros bg
    WHERE bg.BusinessId = b.Id AND bg.BusinessTypeCatalogId = c.Id
);
```

---

## 6. Risk Assessment

| Risk | Likelihood | Mitigation |
|------|-----------|------------|
| Frontend breaks on array JWT claim | High | Phase 3 requires coordinated frontend deploy |
| Existing API consumers send single string | Medium | Accept both `string` and `string[]` during transition |
| Zone defaults become ambiguous with conflicting giros | Low | Union strategy: if *any* giro needs zones, create them |
| Performance: extra JOIN on auth flows | Low | `BusinessGiros` table is tiny; composite PK = clustered index |

---

## 7. Files Requiring Changes

| File | Change Type |
|------|------------|
| `POS.Domain/Enums/BusinessType.cs` | Deprecated → eventually removed |
| `POS.Domain/Models/Business.cs` | Add `BusinessGiros` nav property |
| `POS.Domain/Models/BusinessGiro.cs` | **NEW** — junction entity |
| `POS.Repository/ApplicationDbContext.cs` | Add `DbSet<BusinessGiro>`, fluent config |
| `POS.Services/Service/AuthService.cs` | Refactor Register, BuildDefaultZones, JWT generation |
| `POS.API/Controllers/BusinessController.cs` | Refactor `UpdateType` → `UpdateGiros` |
| `POS.API/Models/AuthRequests.cs` | `BusinessType` → `BusinessTypes` (string array) |
| `POS.Repository/DbInitializer.cs` | Update seed data for BusinessGiro rows |
| Migration file | **NEW** — create table + data migration |

---

## 8. Conclusion

The current model is **strictly single-giro**: one enum value stored as VARCHAR(20), hardcoded in registration logic, zone seeding, and JWT claims. There is no multi-giro capacity today.

The **Many-to-Many junction table** (`BusinessGiros`) is the recommended approach because:
- It leverages the existing `BusinessTypeCatalog` (no duplication)
- Full referential integrity with cascade rules
- Feature aggregation (`HasKitchen`, `HasTables`) becomes a simple LINQ `.Any()` query
- Standard relational pattern with zero surprises for future developers
- The existing column can coexist during migration (backward compatible)

Estimated scope: ~9 files changed, 1 new entity, 1 migration with data backfill.
