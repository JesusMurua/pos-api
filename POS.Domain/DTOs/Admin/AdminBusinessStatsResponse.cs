namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Cross-tenant aggregate snapshot consumed by the super-admin dashboard
/// (<c>GET /api/Admin/businesses/stats</c>). Every count bypasses the
/// BDD-019 tenant query filters via <c>IgnoreQueryFilters</c> at the repo
/// layer; this surface is intended exclusively for the super admin scheme.
/// </summary>
/// <param name="TotalBusinesses">Every business in the system, active or not.</param>
/// <param name="ActiveBusinesses">Subset with <c>IsActive = true</c>.</param>
/// <param name="InactiveBusinesses">Subset with <c>IsActive = false</c>.</param>
/// <param name="ByPlan">
/// Per-plan counts. Always returns one entry per <c>PlanTypeId</c> present
/// in the data; entries with zero are omitted (no synthetic backfill —
/// the FE chart libraries treat missing keys as zero implicitly).
/// </param>
/// <param name="ByMacro">
/// Per-macro-category counts. Same shape semantics as <see cref="ByPlan"/>.
/// </param>
/// <param name="TrialsExpiring7Days">
/// Number of businesses whose <c>TrialEndsAt</c> falls inside the
/// half-open interval <c>(NOW, NOW + 7 days]</c>. Excludes already-expired
/// trials by design — those surface in a future "atención requerida" view.
/// </param>
/// <param name="TrialsExpiring14Days">
/// Number of businesses whose <c>TrialEndsAt</c> falls inside
/// <c>(NOW, NOW + 14 days]</c>. By construction includes the 7-day cohort.
/// </param>
/// <param name="OnboardingCompleted">Businesses with <c>OnboardingCompleted = true</c>.</param>
/// <param name="OnboardingPending">
/// Businesses with <c>OnboardingCompleted = false</c> — collapses
/// catalog states Pending / InProgress / Skipped into one bucket.
/// </param>
/// <param name="TotalUsers">Sum across every business.</param>
/// <param name="TotalProducts">Sum across every branch of every business.</param>
/// <param name="CreatedByMonth">
/// Fixed-length 6-element list ordered oldest → newest covering the
/// current calendar month plus the five prior. Months without creates
/// are backfilled with <c>Count = 0</c> so the FE renders a stable bar
/// chart without conditional logic.
/// </param>
public sealed record AdminBusinessStatsResponse(
    int TotalBusinesses,
    int ActiveBusinesses,
    int InactiveBusinesses,
    IReadOnlyList<PlanDistribution> ByPlan,
    IReadOnlyList<MacroDistribution> ByMacro,
    int TrialsExpiring7Days,
    int TrialsExpiring14Days,
    int OnboardingCompleted,
    int OnboardingPending,
    int TotalUsers,
    int TotalProducts,
    IReadOnlyList<MonthlyCount> CreatedByMonth);

/// <summary>
/// One row of the per-plan distribution. <see cref="PlanTypeCode"/> is the
/// PascalCase code resolved via <c>PlanTypeIds.ToCode</c> so the FE does
/// not maintain its own id→name map.
/// </summary>
public sealed record PlanDistribution(int PlanTypeId, string PlanTypeCode, int Count);

/// <summary>
/// One row of the per-macro distribution. <see cref="PrimaryMacroCategoryCode"/>
/// is the hyphen-case internal code resolved via <c>MacroCategoryIds.ToCode</c>.
/// </summary>
public sealed record MacroDistribution(int PrimaryMacroCategoryId, string PrimaryMacroCategoryCode, int Count);

/// <summary>
/// One bucket of the growth time-series. Year + Month are emitted as
/// integers (no full date) because the bucket represents the entire month.
/// </summary>
public sealed record MonthlyCount(int Year, int Month, int Count);
