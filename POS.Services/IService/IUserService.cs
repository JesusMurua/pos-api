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
}
