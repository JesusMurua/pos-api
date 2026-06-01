namespace POS.Domain.DTOs.Admin;

/// <summary>
/// Row projection for <c>GET /api/Admin/businesses</c>. Joins
/// <c>Business</c> with its Owner user (the first
/// <c>RoleId = UserRoleIds.Owner</c> user, ordered by <c>CreatedAt ASC</c>)
/// so the admin panel can render a tenant directory without follow-up
/// calls. Owner fields are nullable because legacy / partially-seeded rows
/// may legitimately lack an Owner user, and <see cref="OwnerLastLoginAt"/>
/// is additionally null when the Owner has never authenticated.
/// </summary>
public sealed record AdminBusinessListItem(
    int Id,
    string Name,
    string? OwnerEmail,
    string? OwnerName,
    string? OwnerLastLoginAt,
    int PlanTypeId,
    string PlanTypeCode,
    int PrimaryMacroCategoryId,
    string PrimaryMacroCategoryCode,
    string? TrialEndsAt,
    bool IsActive,
    string CreatedAt);
