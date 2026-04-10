using POS.Domain.Exceptions;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

/// <summary>
/// Implements user management operations.
/// </summary>
public class UserService : IUserService
{
    private readonly IUnitOfWork _unitOfWork;

    private static readonly int[] PinRoleIds = { UserRoleIds.Cashier, UserRoleIds.Kitchen, UserRoleIds.Waiter, UserRoleIds.Kiosk };
    private static readonly int[] EmailRoleIds = { UserRoleIds.Owner, UserRoleIds.Manager };

    public UserService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Gets all users for a branch, including the business owner.
    /// </summary>
    public async Task<IEnumerable<UserDto>> GetByBranchAsync(int branchId)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId);
        if (branch == null)
            throw new NotFoundException($"Branch with id {branchId} not found");

        var users = await _unitOfWork.Users.GetAsync(
            u => u.BranchId == branchId
                || (u.BusinessId == branch.BusinessId && u.RoleId == UserRoleIds.Owner));

        return users
            .OrderBy(u => u.RoleId)
            .ThenBy(u => u.Name)
            .Select(MapToDto);
    }

    /// <summary>
    /// Creates a new user with PIN or email authentication.
    /// </summary>
    public async Task<UserDto> CreateAsync(int branchId, CreateUserRequest request)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId);
        if (branch == null)
            throw new NotFoundException($"Branch with id {branchId} not found");

        // Enforce Free plan user limit
        await EnforcePlanUserLimitAsync(branch.BusinessId);

        // Validate PIN-based roles
        if (PinRoleIds.Contains(request.RoleId))
        {
            if (string.IsNullOrEmpty(request.Pin) || request.Pin.Length != 4 || !request.Pin.All(char.IsDigit))
                throw new ValidationException("PIN must be exactly 4 digits");
        }

        // Validate email-based roles
        if (EmailRoleIds.Contains(request.RoleId))
        {
            if (string.IsNullOrEmpty(request.Email))
                throw new ValidationException("Email is required for Owner and Manager roles");

            if (string.IsNullOrEmpty(request.Password))
                throw new ValidationException("Password is required for Owner and Manager roles");

            var existingEmail = await _unitOfWork.Users.GetByEmailAsync(request.Email);
            if (existingEmail != null)
                throw new ValidationException($"Email '{request.Email}' is already in use");
        }

        var user = new User
        {
            BusinessId = branch.BusinessId,
            BranchId = EmailRoleIds.Contains(request.RoleId) ? null : branchId,
            Name = request.Name,
            RoleId = request.RoleId,
            Email = request.Email,
            PasswordHash = !string.IsNullOrEmpty(request.Password)
                ? BCrypt.Net.BCrypt.HashPassword(request.Password)
                : null,
            PinHash = !string.IsNullOrEmpty(request.Pin)
                ? BCrypt.Net.BCrypt.HashPassword(request.Pin)
                : null,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Users.AddAsync(user);
        await _unitOfWork.SaveChangesAsync();

        // Auto-assign the user to a default branch via UserBranch
        var defaultBranch = await ResolveDefaultBranchAsync(branch.BusinessId);
        if (defaultBranch != null)
        {
            await _unitOfWork.UserBranches.AddAsync(new UserBranch
            {
                UserId = user.Id,
                BranchId = defaultBranch.Id,
                IsDefault = true
            });
            await _unitOfWork.SaveChangesAsync();
        }

        return MapToDto(user);
    }

    /// <summary>
    /// Updates an existing user.
    /// </summary>
    public async Task<UserDto> UpdateAsync(int id, UpdateUserRequest request)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(id);
        if (user == null)
            throw new NotFoundException($"User with id {id} not found");

        user.Name = request.Name;
        user.RoleId = request.RoleId;
        user.IsActive = request.IsActive;

        if (!string.IsNullOrEmpty(request.Pin))
        {
            if (request.Pin.Length != 4 || !request.Pin.All(char.IsDigit))
                throw new ValidationException("PIN must be exactly 4 digits");

            user.PinHash = BCrypt.Net.BCrypt.HashPassword(request.Pin);
        }

        if (!string.IsNullOrEmpty(request.Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync();

        return MapToDto(user);
    }

    /// <summary>
    /// Toggles user active status.
    /// </summary>
    public async Task<bool> ToggleActiveAsync(int id)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(id);
        if (user == null)
            throw new NotFoundException($"User with id {id} not found");

        // Prevent deactivating the last active owner
        if (user.IsActive && user.RoleId == UserRoleIds.Owner)
        {
            var owners = await _unitOfWork.Users.GetAsync(
                u => u.BusinessId == user.BusinessId && u.RoleId == UserRoleIds.Owner && u.IsActive);

            if (owners.Count() <= 1)
                throw new ValidationException("Cannot deactivate the last active owner");
        }

        user.IsActive = !user.IsActive;

        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync();

        return user.IsActive;
    }

    /// <summary>
    /// Gets all branch assignments for a user.
    /// </summary>
    public async Task<IEnumerable<UserBranchDto>> GetUserBranchesAsync(int userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException($"User with id {userId} not found");

        var userBranches = await _unitOfWork.UserBranches.GetByUserIdAsync(userId);

        return userBranches
            .Where(ub => ub.Branch != null)
            .Select(ub => new UserBranchDto
            {
                BranchId = ub.BranchId,
                BranchName = ub.Branch!.Name,
                IsDefault = ub.IsDefault
            });
    }

    /// <summary>
    /// Replaces all branch assignments for a user.
    /// </summary>
    public async Task<IEnumerable<UserBranchDto>> SetUserBranchesAsync(int userId, int[] branchIds, int defaultBranchId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException($"User with id {userId} not found");

        if (branchIds.Length == 0)
            throw new ValidationException("At least one branch must be assigned");

        if (!branchIds.Contains(defaultBranchId))
            throw new ValidationException("Default branch must be included in the branch list");

        // Remove existing assignments
        var existing = await _unitOfWork.UserBranches.GetByUserIdAsync(userId);
        foreach (var ub in existing)
            _unitOfWork.UserBranches.Delete(ub);

        // Add new assignments
        foreach (var branchId in branchIds.Distinct())
        {
            await _unitOfWork.UserBranches.AddAsync(new UserBranch
            {
                UserId = userId,
                BranchId = branchId,
                IsDefault = branchId == defaultBranchId
            });
        }

        await _unitOfWork.SaveChangesAsync();

        return await GetUserBranchesAsync(userId);
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Throws PlanLimitExceededException if the business is on the Free plan
    /// and already has the maximum allowed users.
    /// </summary>
    private async Task EnforcePlanUserLimitAsync(int businessId)
    {
        var business = await _unitOfWork.Business.GetByIdAsync(businessId);
        if (business == null || business.PlanTypeId != PlanTypeIds.Free)
            return;

        var users = await _unitOfWork.Users.GetAsync(u => u.BusinessId == businessId);
        if (users.Count() >= PlanLimits.FreeMaxUsers)
            throw new PlanLimitExceededException("usuarios", PlanLimits.FreeMaxUsers, "Free");
    }

    /// <summary>
    /// Resolves the default branch for a business: matrix branch first, then lowest ID.
    /// </summary>
    private async Task<Branch?> ResolveDefaultBranchAsync(int businessId)
    {
        var branches = await _unitOfWork.Branches.GetAsync(
            b => b.BusinessId == businessId && b.IsActive);

        return branches.OrderByDescending(b => b.IsMatrix).ThenBy(b => b.Id).FirstOrDefault();
    }

    private static UserDto MapToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            RoleId = user.RoleId,
            BranchId = user.BranchId,
            IsActive = user.IsActive,
            HasPin = user.PinHash != null,
            HasEmail = user.Email != null,
            CreatedAt = user.CreatedAt
        };
    }

    #endregion
}
