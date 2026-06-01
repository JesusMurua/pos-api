namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Lightweight branch projection embedded in
/// <see cref="AdminBusinessDetailResponse"/>. Carries only the
/// identifying + activity fields the admin UI needs to render the
/// branch list under a tenant detail view.
/// </summary>
public sealed record BranchInfo(
    int Id,
    string Name,
    bool IsActive,
    string CreatedAt);
