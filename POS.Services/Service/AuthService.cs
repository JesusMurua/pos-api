using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Domain.Settings;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtSettings _jwtSettings;

    public AuthService(IUnitOfWork unitOfWork, IOptions<JwtSettings> jwtSettings)
    {
        _unitOfWork = unitOfWork;
        _jwtSettings = jwtSettings.Value;
    }

    #region Public API Methods

    /// <summary>
    /// Authenticates an owner by email and password.
    /// </summary>
    public async Task<AuthResponse> EmailLoginAsync(string email, string password)
    {
        var user = await _unitOfWork.Users.GetByEmailAsync(email);

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
            throw new ValidationException("Invalid email or password");

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            throw new ValidationException("Invalid email or password");

        var (currentBranchId, branches) = await ResolveBranchesAsync(user);

        var token = GenerateToken(user, currentBranchId, branches, TimeSpan.FromDays(_jwtSettings.OwnerExpirationDays));

        return new AuthResponse
        {
            Token = token,
            Role = user.Role.ToString(),
            Name = user.Name,
            BusinessId = user.BusinessId,
            CurrentBranchId = currentBranchId,
            Branches = branches
        };
    }

    /// <summary>
    /// Authenticates a staff member by branch PIN.
    /// </summary>
    public async Task<AuthResponse> PinLoginAsync(int branchId, string pin)
    {
        var users = await _unitOfWork.Users.GetActiveByBranchAsync(branchId);

        User? matchedUser = null;
        foreach (var user in users)
        {
            if (!string.IsNullOrEmpty(user.PinHash) && BCrypt.Net.BCrypt.Verify(pin, user.PinHash))
            {
                matchedUser = user;
                break;
            }
        }

        if (matchedUser == null)
            throw new ValidationException("Invalid PIN");

        var (currentBranchId, branches) = await ResolveBranchesAsync(matchedUser);

        var token = GenerateToken(matchedUser, currentBranchId, branches, TimeSpan.FromHours(_jwtSettings.PinExpirationHours));

        return new AuthResponse
        {
            Token = token,
            Role = matchedUser.Role.ToString(),
            Name = matchedUser.Name,
            BusinessId = matchedUser.BusinessId,
            CurrentBranchId = currentBranchId,
            Branches = branches
        };
    }

    /// <summary>
    /// Switches the authenticated user to a different branch and returns a new JWT.
    /// </summary>
    public async Task<AuthResponse> SwitchBranchAsync(int userId, int branchId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || !user.IsActive)
            throw new NotFoundException($"User with id {userId} not found");

        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId);
        if (branch == null || !branch.IsActive)
            throw new NotFoundException($"Branch with id {branchId} not found");

        if (branch.BusinessId != user.BusinessId)
            throw new UnauthorizedException("Branch does not belong to the user's business");

        // Owner/Manager can switch to any branch in their business
        var isAdminRole = user.Role == UserRole.Owner || user.Role == UserRole.Manager;

        if (!isAdminRole)
        {
            var userBranches = await _unitOfWork.UserBranches.GetByUserIdAsync(userId);
            if (!userBranches.Any(ub => ub.BranchId == branchId))
                throw new UnauthorizedException("User is not assigned to the requested branch");
        }

        var (_, branches) = await ResolveBranchesAsync(user);

        var expiration = isAdminRole
            ? TimeSpan.FromDays(_jwtSettings.OwnerExpirationDays)
            : TimeSpan.FromHours(_jwtSettings.PinExpirationHours);

        var token = GenerateToken(user, branchId, branches, expiration);

        return new AuthResponse
        {
            Token = token,
            Role = user.Role.ToString(),
            Name = user.Name,
            BusinessId = user.BusinessId,
            CurrentBranchId = branchId,
            Branches = branches
        };
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Resolves the current branch and all available branches for a user.
    /// Uses UserBranch table; falls back to first business branch for Owners.
    /// </summary>
    private async Task<(int currentBranchId, List<BranchSummary> branches)> ResolveBranchesAsync(User user)
    {
        var userBranches = (await _unitOfWork.UserBranches.GetByUserIdAsync(user.Id)).ToList();

        if (userBranches.Count > 0)
        {
            var defaultBranch = userBranches.FirstOrDefault(ub => ub.IsDefault) ?? userBranches.First();
            var branchList = userBranches
                .Where(ub => ub.Branch != null)
                .Select(ub => new BranchSummary { Id = ub.BranchId, Name = ub.Branch!.Name })
                .ToList();

            return (defaultBranch.BranchId, branchList);
        }

        // Fallback for Owner without UserBranch records: use first branch of the business
        if (user.Role == UserRole.Owner)
        {
            var businessBranches = await _unitOfWork.Branches.GetAsync(
                b => b.BusinessId == user.BusinessId && b.IsActive);

            var firstBranch = businessBranches.OrderBy(b => b.Id).FirstOrDefault();
            if (firstBranch != null)
            {
                var branchList = businessBranches
                    .OrderBy(b => b.Id)
                    .Select(b => new BranchSummary { Id = b.Id, Name = b.Name })
                    .ToList();

                return (firstBranch.Id, branchList);
            }
        }

        throw new ValidationException("User has no assigned branch");
    }

    /// <summary>
    /// Generates a JWT token with user claims, including branchId and branches array.
    /// </summary>
    private string GenerateToken(User user, int branchId, List<BranchSummary> branches, TimeSpan expiration)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var branchesJson = JsonSerializer.Serialize(
            branches.Select(b => new { id = b.Id, name = b.Name }));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Role, user.Role.ToString()),
            new(ClaimTypes.Name, user.Name),
            new("businessId", user.BusinessId.ToString()),
            new("branchId", branchId.ToString()),
            new("branches", branchesJson)
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(expiration),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    #endregion
}
