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

Per-business snapshot cached in `IMemoryCache` (`FeatureGate::{businessId}`, 5 min TTL).
Invalidated by `IFeatureGateService.Invalidate(businessId)` on plan changes (Stripe
worker) and giro changes (`BusinessService`).

> **Future (PR-B, deferred):** load the global matrices once into memory and resolve
> per-tenant via in-memory lookups, with a generation token enabling a cheap
> `InvalidateAll()` for the admin console. Not implemented yet — matrices are still
> shipped via the bootstrap seed.

## Out of scope (future)

- **Admin console** to edit matrices without a deploy (needs the seed migrated to
  bootstrap-only / additive first, plus `InvalidateAll`).
- **Per-tenant override** (a single business opting outside its cluster).
- **Sub-giro granularity** finer than cluster (a `BusinessTypeCatalogFeature` axis).
