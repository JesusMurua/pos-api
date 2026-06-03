# Feature Matrix Architecture

How the backend decides which features a tenant gets. The resolver
([`FeatureGateService.BuildSnapshotAsync`](../POS.Services/Service/FeatureGateService.cs))
combines **three axes** plus an override layer, caches the result per business,
and feeds both the JWT `features` claim and server-side enforcement.

## Axes

| Axis | Entity | Key | Meaning |
|------|--------|-----|---------|
| Plan | `PlanFeatureMatrix` | `(PlanTypeId, FeatureId)` | `IsEnabled` + `DefaultLimit` per plan tier |
| Macro | `BusinessTypeFeature` | `(MacroCategoryId, FeatureId)` | **presence = applicable** to that macro; optional `Limit` |
| Cluster | `ClusterFeature` | `(ClusterCode, FeatureId)` | **presence = applicable** to that sub-giro cluster |
| Override | `PlanBusinessTypeFeatureOverride` | `(PlanTypeId, MacroCategoryId, FeatureId)` | final word for that `(plan, macro)` tuple |

- `FeatureCatalog` mirrors the `FeatureKey` enum 1:1. **The enum is the source of
  truth**; the catalog row is its DB projection (indexed by `feature.Key`).
- A business's **macro** is `Business.PrimaryMacroCategoryId`. Its **clusters** are
  the distinct `BusinessTypeCatalog.ClusterCode` of its `BusinessGiro` sub-giros
  (clusters exist only for Macro 4 / Services — see
  [`ClusterCodes`](../POS.Domain/Helpers/ClusterCodes.cs)).

## Resolution

For each feature:

```
applicable      = a BusinessTypeFeature(macro, feature) row exists   // strict presence; absent ⇒ off
planEnabled     = PlanFeatureMatrix(plan, feature).IsEnabled

clusterRuled    = at least one ClusterFeature row exists for the feature
clusterApplies  = !clusterRuled                       → true          // 27 features without rules: unaffected
                | clusterRuled && businessClusters==∅  → false         // FAIL-CLOSED
                | otherwise                            → businessClusters ∩ clustersOf(feature) ≠ ∅

isEnabled = override(plan, macro) exists ? override.IsEnabled
                                         : planEnabled && applicable && clusterApplies
```

### Deliberate asymmetry between macro and cluster

- **Macro** is *strict presence*: a feature with no `BusinessTypeFeature` row for the
  macro is OFF. This is the pre-existing model — do not add a fallback.
- **Cluster** is *additive pass-through*: a feature only becomes cluster-gated when
  at least one `ClusterFeature` row exists for it. Features with no cluster rules keep
  pure Plan × Macro behavior (backward compatible).

### Fail-closed on empty clusters

A business with no clusters (mid-onboarding before its sub-giro is set) gets
cluster-gated features **disabled**, not leaked. This is safe because of the
invariant below: a *configured* business always has a catalog sub-giro, so the only
window with empty clusters is an in-progress onboarding that has configured nothing
yet.

### Override limitation

`PlanBusinessTypeFeatureOverride` is the final word and **bypasses the cluster check**
(it is a Plan × Macro escape hatch — there is no cluster-level override). None of the
cluster-gated features currently has an override.

## Invariant: a configured business always has a catalog sub-giro

Enforced at every write path so the cluster always resolves:

- `BusinessService.UpdateGiroAsync` — requires ≥1 catalog sub-giro;
  `CustomGiroDescription` is additive free text, never a substitute.
- `BusinessService.EnsureCanCompleteOnboardingAsync` — guards
  `POST /api/business/complete-onboarding` and `PUT /api/business/onboarding-step`
  (`StatusId == 3`).
- `AuthService` admin flow — `MarkOnboardingComplete=true` requires `SubGiroIds`.

## Seeded cluster rules

Seeded additively (insert-if-missing, never overwrite/delete) in
[`DbInitializer.UpsertFeatureMatrixAsync`](../POS.Repository/DbInitializer.cs):

| Feature | Clusters |
|---------|----------|
| `RealtimeAccessControl` | `fitness` |
| `MaxReceptionsPerBranch` | `fitness` |
| `AppointmentReminders` | `beauty`, `health`, `pets`, `automotive`, `professional`, `education` |

## JWT staleness vs. server enforcement

The `features` claim is **baked into the JWT at issuance** (login / token refresh /
device activation). Editing the matrix does **not** retroactively change live tokens.

- **Server-side enforcement** (`[RequiresFeature]` → `EnforceAsync`, hub
  `IsEnabledAsync`) re-reads the snapshot per request — it reflects matrix changes as
  soon as the per-business cache is invalidated.
- **The JWT claim** is a UI hint only and updates on the next token issuance.

Never gate security on the claim alone; keep it on `EnforceAsync`.

## Caching

Two-level, generation-versioned (`FeatureCacheGeneration`, a process-wide
`Interlocked` counter):

- **Global matrices** (`FeatureMatrix::{gen}`, 1 h TTL): the four matrices +
  `FeatureCatalog` loaded once into in-memory dictionaries. They are small and
  tenant-independent, so every snapshot resolves against this copy instead of
  re-querying the DB.
- **Per-business snapshot** (`FeatureGate::{gen}::{businessId}`, 5 min TTL): the
  resolved feature set. Building one only reads the tenant's `(PlanTypeId,
  PrimaryMacroCategoryId)` + its clusters, then resolves in memory.

Invalidation:

- `Invalidate(businessId)` — drops one snapshot. Called on plan changes (Stripe
  worker) and giro changes (`BusinessService`).
- `InvalidateAll()` — bumps the generation, orphaning the global matrix cache and
  every snapshot at once (O(1), no prefix scan). Call after any matrix/override
  edit, since a matrix change affects every tenant. Orphans expire by their own
  TTL; the next read repopulates under the new generation.

## Seeding (bootstrap-only)

`DbInitializer.UpsertFeatureMatrixAsync` runs on every boot but is **insert-if-missing
per row**: it adds rows that don't exist yet (so a new `FeatureKey` gets its catalog +
default matrix rows on deploy) and **never overwrites or deletes** existing rows. The
DB is the source of truth for values — admin edits survive a re-seed. Rule *value*
changes therefore ship via the admin endpoints or an explicit data migration, not via
the seed.

## Admin endpoints

Ops-only CRUD under `api/Admin`, authenticated by the **`X-Admin-Token`** scheme (no
`SuperAdmin` role exists). Every mutation is audited to `FeatureMatrixAuditLog`
(attributed to the token's `token_id` claim, one row per changed entity) and calls
`InvalidateAll()`.

| Endpoint | Notes |
|----------|-------|
| `GET/PUT feature-catalog`, `PUT feature-catalog/{id}` | **Metadata only** (Name/Description/ResourceLabel/SortOrder). No POST/DELETE — features are `FeatureKey`-bound. |
| `GET/PUT plan-feature-matrix` | Flag matrix: `{ planTypeId, featureId, isEnabled, defaultLimit }`. Bulk **upsert-merge**. |
| `GET/PUT business-type-feature-matrix` | Presence: `{ macroCategoryId, featureId, isApplicable, limit? }`. `isApplicable=false` deletes the row. |
| `GET/PUT cluster-feature-matrix` | Presence: `{ clusterCode, featureId, isApplicable }`. GET envelope includes the full cluster slug list. |
| `GET/POST/PUT/DELETE plan-business-type-overrides` | Composite key `{planTypeId}/{macroCategoryId}/{featureId}`. |
| `GET feature-matrix/preview-impact?axis=cluster&...` | Recomputes the resolver to count businesses whose outcome **effectively** flips; returns `affectedCount`, `breakdownByPlan`, `sampleBusinessIds` (≤50). Cluster axis only. |
| `GET feature-matrix/audit-log?from=&to=&axis=&page=&pageSize=` | Paginated, DESC by `ChangedAt`. |

Bulk PUTs are **upsert-merge**: only the supplied entries are applied; unlisted rows
are untouched (never a full replace). Invalid `featureId`/`planType`/`macro`/`cluster`
→ 400.

## Out of scope (future)

- **Per-tenant override** (a single business opting outside its cluster).
- **Sub-giro granularity** finer than cluster (a `BusinessTypeCatalogFeature` axis).
- **Admin UI** (fino-admin) — these endpoints are the backend it will consume.
