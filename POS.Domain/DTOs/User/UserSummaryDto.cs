namespace POS.Domain.DTOs.User;

/// <summary>
/// Lightweight user projection for nesting inside aggregate DTOs (e.g. cash
/// session/movement responses). Includes <c>RoleId</c> so the frontend can
/// render role-specific labels without a follow-up call. Email is intentionally
/// omitted to keep PII out of frequently-fetched audit endpoints, and to keep
/// this distinct from the heavier <c>UserDto</c> used by the user-management
/// CRUD endpoints.
/// </summary>
public class UserSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int RoleId { get; set; }
}
