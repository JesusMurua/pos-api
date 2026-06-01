using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing users.
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Gets all users for a branch, including the business owner.
    /// </summary>
    Task<IEnumerable<UserDto>> GetByBranchAsync(int branchId);

    /// <summary>
    /// Creates a new user with PIN or email authentication.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">
    /// Thrown when email already exists or PIN is invalid.</exception>
    Task<UserDto> CreateAsync(int branchId, CreateUserRequest request);

    /// <summary>
    /// Updates an existing user.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">
    /// Thrown when user not found.</exception>
    Task<UserDto> UpdateAsync(int id, UpdateUserRequest request);

    /// <summary>
    /// Toggles user active status.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">
    /// Thrown when user not found.</exception>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">
    /// Thrown when trying to deactivate the last active owner.</exception>
    Task<bool> ToggleActiveAsync(int id);

    /// <summary>
    /// Gets all branch assignments for a user.
    /// </summary>
    Task<IEnumerable<UserBranchDto>> GetUserBranchesAsync(int userId);

    /// <summary>
    /// Replaces all branch assignments for a user.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">
    /// Thrown when defaultBranchId is not in branchIds.</exception>
    Task<IEnumerable<UserBranchDto>> SetUserBranchesAsync(int userId, int[] branchIds, int defaultBranchId);

    /// <summary>
    /// Records the first-time dismissal of the welcome screen by setting
    /// <c>User.WelcomeShownAt = DateTime.UtcNow</c>. Idempotent: if the
    /// timestamp is already populated the existing value is preserved so
    /// subsequent calls report the original first-seen moment rather than
    /// overwriting it. Returns the effective timestamp the caller can
    /// surface to the SPA without an extra read.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">
    /// Thrown when <paramref name="userId"/> does not resolve to an
    /// existing User.
    /// </exception>
    Task<DateTime> MarkWelcomeShownAsync(int userId);

    /// <summary>
    /// Resets the credential password of the tenant's Owner user (oldest
    /// <c>RoleId = UserRoleIds.Owner</c> by <c>CreatedAt</c>). When
    /// <paramref name="newPassword"/> is null the service generates a
    /// 12-character cryptographically-random password from the alphabet
    /// <c>[A-Za-z0-9!@#$]</c>. Validates min-length 8 (same complexity
    /// floor as the public Register flow). Returns the effective
    /// plaintext password so the caller can surface it to the admin
    /// UI for relay to the customer.
    /// </summary>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">
    /// Thrown when the business has no Owner user.
    /// </exception>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">
    /// Thrown when a caller-provided <paramref name="newPassword"/> is
    /// shorter than 8 characters.
    /// </exception>
    Task<string> ResetOwnerPasswordAsync(int businessId, string? newPassword);
}
