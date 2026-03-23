using POS.Domain.Enums;
using POS.Domain.Exceptions;
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

    private static readonly UserRole[] PinRoles = { UserRole.Cashier, UserRole.Kitchen, UserRole.Waiter, UserRole.Kiosk };
    private static readonly UserRole[] EmailRoles = { UserRole.Owner, UserRole.Manager };

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
                || (u.BusinessId == branch.BusinessId && u.Role == UserRole.Owner));

        return users
            .OrderBy(u => u.Role)
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

        // Validate PIN-based roles
        if (PinRoles.Contains(request.Role))
        {
            if (string.IsNullOrEmpty(request.Pin) || request.Pin.Length != 4 || !request.Pin.All(char.IsDigit))
                throw new ValidationException("PIN must be exactly 4 digits");
        }

        // Validate email-based roles
        if (EmailRoles.Contains(request.Role))
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
            BranchId = EmailRoles.Contains(request.Role) ? null : branchId,
            Name = request.Name,
            Role = request.Role,
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
        user.Role = request.Role;
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
        if (user.IsActive && user.Role == UserRole.Owner)
        {
            var owners = await _unitOfWork.Users.GetAsync(
                u => u.BusinessId == user.BusinessId && u.Role == UserRole.Owner && u.IsActive);

            if (owners.Count() <= 1)
                throw new ValidationException("Cannot deactivate the last active owner");
        }

        user.IsActive = !user.IsActive;

        _unitOfWork.Users.Update(user);
        await _unitOfWork.SaveChangesAsync();

        return user.IsActive;
    }

    #endregion

    #region Private Helper Methods

    private static UserDto MapToDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            RoleName = user.Role.ToString(),
            BranchId = user.BranchId,
            IsActive = user.IsActive,
            HasPin = user.PinHash != null,
            HasEmail = user.Email != null,
            CreatedAt = user.CreatedAt
        };
    }

    #endregion
}
