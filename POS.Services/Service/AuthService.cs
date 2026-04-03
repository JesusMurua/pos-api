using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
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
    private readonly IEmailService _emailService;

    public AuthService(
        IUnitOfWork unitOfWork,
        IOptions<JwtSettings> jwtSettings,
        IEmailService emailService)
    {
        _unitOfWork = unitOfWork;
        _jwtSettings = jwtSettings.Value;
        _emailService = emailService;
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

        var business = await _unitOfWork.Business.GetByIdAsync(user.BusinessId);
        var (currentBranchId, branches) = await ResolveBranchesAsync(user);

        var subscription = await ResolveSubscriptionAsync(user.BusinessId);
        var planType = subscription?.PlanType ?? "Free";
        var token = GenerateToken(user, business!, currentBranchId, branches, TimeSpan.FromDays(_jwtSettings.OwnerExpirationDays), planType);

        return new AuthResponse
        {
            Token = token,
            Role = user.Role.ToString(),
            Name = user.Name,
            BusinessId = user.BusinessId,
            CurrentBranchId = currentBranchId,
            Branches = branches,
            PlanType = planType,
            BusinessType = business!.BusinessType.ToString(),
            TrialEndsAt = subscription?.TrialEndsAt.ToString("o"),
            SubscriptionStatus = subscription?.Status,
            OnboardingCompleted = business.OnboardingCompleted
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

        var business = await _unitOfWork.Business.GetByIdAsync(matchedUser.BusinessId);
        var (currentBranchId, branches) = await ResolveBranchesAsync(matchedUser);

        var subscription = await ResolveSubscriptionAsync(matchedUser.BusinessId);
        var planType = subscription?.PlanType ?? "Free";
        var token = GenerateToken(matchedUser, business!, currentBranchId, branches, TimeSpan.FromHours(_jwtSettings.PinExpirationHours), planType);

        return new AuthResponse
        {
            Token = token,
            Role = matchedUser.Role.ToString(),
            Name = matchedUser.Name,
            BusinessId = matchedUser.BusinessId,
            CurrentBranchId = currentBranchId,
            Branches = branches,
            PlanType = planType,
            BusinessType = business!.BusinessType.ToString(),
            TrialEndsAt = subscription?.TrialEndsAt.ToString("o"),
            SubscriptionStatus = subscription?.Status,
            OnboardingCompleted = business.OnboardingCompleted
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

        var isAdminRole = user.Role == UserRole.Owner || user.Role == UserRole.Manager;

        if (!isAdminRole)
        {
            var userBranches = await _unitOfWork.UserBranches.GetByUserIdAsync(userId);
            if (!userBranches.Any(ub => ub.BranchId == branchId))
                throw new UnauthorizedException("User is not assigned to the requested branch");
        }

        var business = await _unitOfWork.Business.GetByIdAsync(user.BusinessId);
        var (_, branches) = await ResolveBranchesAsync(user);

        var expiration = isAdminRole
            ? TimeSpan.FromDays(_jwtSettings.OwnerExpirationDays)
            : TimeSpan.FromHours(_jwtSettings.PinExpirationHours);

        var subscription = await ResolveSubscriptionAsync(user.BusinessId);
        var planType = subscription?.PlanType ?? "Free";
        var token = GenerateToken(user, business!, branchId, branches, expiration, planType);

        return new AuthResponse
        {
            Token = token,
            Role = user.Role.ToString(),
            Name = user.Name,
            BusinessId = user.BusinessId,
            CurrentBranchId = branchId,
            Branches = branches,
            PlanType = planType,
            BusinessType = business!.BusinessType.ToString(),
            TrialEndsAt = subscription?.TrialEndsAt.ToString("o"),
            SubscriptionStatus = subscription?.Status,
            OnboardingCompleted = business.OnboardingCompleted
        };
    }

    /// <summary>
    /// Registers a new business with owner account and matrix branch.
    /// Atomic: single transaction, single SaveChangesAsync, full rollback on failure.
    /// </summary>
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        ValidatePasswordComplexity(request.Password);

        var existingUser = await _unitOfWork.Users.GetByEmailAsync(request.Email);
        if (existingUser != null)
            throw new ValidationException("EMAIL_ALREADY_EXISTS");

        if (!Enum.TryParse<BusinessType>(request.BusinessType, true, out var businessType))
            businessType = BusinessType.General;

        var hasKitchen = businessType is BusinessType.Restaurant or BusinessType.Cafe or BusinessType.Bar or BusinessType.FoodTruck;
        var hasTables = businessType is BusinessType.Restaurant or BusinessType.Cafe or BusinessType.Bar;

        // ── Build entire entity graph with navigation properties ──
        var business = new Business
        {
            Name = request.BusinessName,
            BusinessType = businessType,
            PlanType = PlanType.Free,
            TrialEndsAt = DateTime.UtcNow.AddMonths(3),
            TrialUsed = false,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var branch = new Branch
        {
            Business = business,
            Name = $"{request.BusinessName} Principal",
            IsMatrix = true,
            IsActive = true,
            HasKitchen = hasKitchen,
            HasTables = hasTables,
            FolioCounter = 0,
            CreatedAt = DateTime.UtcNow
        };

        var user = new User
        {
            Business = business,
            Name = request.OwnerName,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRole.Owner,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var userBranch = new UserBranch
        {
            User = user,
            Branch = branch,
            IsDefault = true
        };

        // Default zones
        var zones = BuildDefaultZones(branch, businessType);

        // Default category so the POS frontend doesn't crash on empty state
        var defaultCategory = new Category
        {
            Branch = branch,
            Name = "General",
            Icon = "pi-tag",
            SortOrder = 1,
            IsActive = true
        };

        // ── Atomic transaction: single SaveChangesAsync ──
        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        try
        {
            await _unitOfWork.Business.AddAsync(business);
            await _unitOfWork.Branches.AddAsync(branch);
            await _unitOfWork.Users.AddAsync(user);
            await _unitOfWork.UserBranches.AddAsync(userBranch);
            await _unitOfWork.Categories.AddAsync(defaultCategory);

            if (zones.Count > 0)
                await _unitOfWork.Zones.AddRangeAsync(zones);

            // Default table so table-service businesses have at least one
            if (hasTables)
            {
                var defaultTable = new RestaurantTable
                {
                    Branch = branch,
                    Zone = zones.FirstOrDefault(),
                    Name = "Mesa 1",
                    Capacity = 4,
                    Status = "available",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                await _unitOfWork.RestaurantTables.AddAsync(defaultTable);
            }

            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            throw new ValidationException("EMAIL_ALREADY_EXISTS");
        }

        // Generate JWT
        var branches = new List<BranchSummary> { new() { Id = branch.Id, Name = branch.Name } };
        var token = GenerateToken(user, business, branch.Id, branches, TimeSpan.FromDays(_jwtSettings.OwnerExpirationDays), "Free");

        // Fire-and-forget: welcome email — never blocks the response
        _ = _emailService.SendWelcomeEmailAsync(request.Email, request.OwnerName, request.BusinessName);

        return new AuthResponse
        {
            Token = token,
            Role = user.Role.ToString(),
            Name = user.Name,
            BusinessId = business.Id,
            CurrentBranchId = branch.Id,
            Branches = branches,
            PlanType = business.PlanType.ToString(),
            BusinessType = business.BusinessType.ToString(),
            TrialEndsAt = business.TrialEndsAt?.ToString("o"),
            OnboardingCompleted = business.OnboardingCompleted
        };
    }

    #endregion

    #region Private Helper Methods

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

    private async Task<Subscription?> ResolveSubscriptionAsync(int businessId)
    {
        return await _unitOfWork.Subscriptions.GetByBusinessIdAsync(businessId);
    }

    private string GenerateToken(User user, Business business, int branchId, List<BranchSummary> branches, TimeSpan expiration, string planType)
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
            new("branches", branchesJson),
            new("planType", planType),
            new("businessType", business.BusinessType.ToString()),
            new("trialEndsAt", business.TrialEndsAt?.ToString("o") ?? ""),
            new("onboardingCompleted", business.OnboardingCompleted.ToString().ToLower())
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

    private static readonly Regex PasswordComplexityRegex = new(
        @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d).{8,}$",
        RegexOptions.Compiled);

    private static void ValidatePasswordComplexity(string password)
    {
        if (!PasswordComplexityRegex.IsMatch(password))
            throw new ValidationException(
                "Password must be at least 8 characters and contain at least 1 uppercase letter, 1 lowercase letter, and 1 number");
    }

    /// <summary>
    /// Builds default zone entities (not persisted yet) using navigation properties.
    /// </summary>
    private static List<Zone> BuildDefaultZones(Branch branch, BusinessType businessType)
    {
        if (businessType is BusinessType.Retail or BusinessType.FoodTruck or BusinessType.General)
            return [];

        var zones = new List<Zone>
        {
            new() { Branch = branch, Name = "Salón", Type = ZoneType.Salon, SortOrder = 1, IsActive = true }
        };

        if (businessType == BusinessType.Bar)
            zones.Add(new Zone { Branch = branch, Name = "Barra", Type = ZoneType.BarSeats, SortOrder = 2, IsActive = true });

        zones.Add(new Zone { Branch = branch, Name = "Terraza", Type = ZoneType.Other, SortOrder = 3, IsActive = false });

        return zones;
    }

    #endregion
}
