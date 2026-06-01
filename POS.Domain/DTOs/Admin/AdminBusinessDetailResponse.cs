using POS.Domain.DTOs.Auth;

namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Full tenant projection consumed by the super-admin detail view
/// (<c>GET /api/Admin/businesses/{id}</c>). Combines identity, plan,
/// onboarding, trial, fiscal, snapshot, owner contact, and branch
/// inventory in one document so the FE can render a single screen
/// without follow-up round-trips. Status-mutation endpoints
/// (PATCH .../status, .../plan, .../trial) refetch and return the
/// same shape for consistency.
/// </summary>
public sealed record AdminBusinessDetailResponse(
    int Id,
    string Name,
    bool IsActive,
    string CreatedAt,
    string CountryCode,

    int PlanTypeId,
    string PlanTypeCode,
    int PrimaryMacroCategoryId,
    string PrimaryMacroCategoryCode,
    string? CustomGiroDescription,
    IReadOnlyList<int> SubGiroIds,

    bool OnboardingCompleted,
    int OnboardingStatusId,
    int CurrentOnboardingStep,

    string? TrialEndsAt,
    bool TrialUsed,

    string? Rfc,
    string? TaxRegime,
    string? LegalName,
    bool InvoicingEnabled,

    BusinessSnapshot Snapshot,

    string? OwnerEmail,
    string? OwnerName,
    string? OwnerLastLoginAt,

    IReadOnlyList<BranchInfo> Branches);
