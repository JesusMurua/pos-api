namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Response from <c>POST /api/Admin/businesses</c>. Surfaces the
/// freshly-created tenant identifiers, the resolved plan / macro codes,
/// the final onboarding state (which mirrors the
/// <c>MarkOnboardingComplete</c> flag on the request), and — when the
/// caller opted in via <c>IncludeOwnerJwt</c> — the Owner JWT so the
/// super admin can drop straight into the new tenant's POS without an
/// extra login round-trip.
/// <para>
/// <see cref="OwnerJwt"/> is intentionally nullable and only populated
/// when <c>IncludeOwnerJwt</c> was true. Under the global
/// <c>WhenWritingNull</c> JSON policy the field is omitted from the wire
/// payload entirely when null, so a default-flag response does not even
/// hint at its existence in admin-side network logs.
/// </para>
/// </summary>
public sealed record AdminCreateBusinessResponse(
    int BusinessId,
    string OwnerEmail,
    string OwnerName,
    int PlanTypeId,
    string PlanTypeCode,
    int PrimaryMacroCategoryId,
    string PrimaryMacroCategoryCode,
    string? TrialEndsAt,
    string CreatedAt,
    bool OnboardingCompleted,
    int OnboardingStatusId,
    string? OwnerJwt);
