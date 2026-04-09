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
using POS.Domain.Helpers;
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
        var effectivePlanTypeId = subscription?.PlanTypeId ?? business!.PlanTypeId;
        var planType = PlanTypeIds.ToCode(effectivePlanTypeId);
        var token = GenerateToken(user, business!, currentBranchId, branches, TimeSpan.FromDays(_jwtSettings.OwnerExpirationDays), planType);

        return new AuthResponse
        {
            Token = token,
            RoleId = user.RoleId,
            Name = user.Name,
            BusinessId = user.BusinessId,
            CurrentBranchId = currentBranchId,
            Branches = branches,
            PlanTypeId = effectivePlanTypeId,
            BusinessTypeId = business!.BusinessTypeId,
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
        var effectivePlanTypeId = subscription?.PlanTypeId ?? business!.PlanTypeId;
        var planType = PlanTypeIds.ToCode(effectivePlanTypeId);
        var token = GenerateToken(matchedUser, business!, currentBranchId, branches, TimeSpan.FromHours(_jwtSettings.PinExpirationHours), planType);

        return new AuthResponse
        {
            Token = token,
            RoleId = matchedUser.RoleId,
            Name = matchedUser.Name,
            BusinessId = matchedUser.BusinessId,
            CurrentBranchId = currentBranchId,
            Branches = branches,
            PlanTypeId = effectivePlanTypeId,
            BusinessTypeId = business!.BusinessTypeId,
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

        var isAdminRole = user.RoleId == UserRoleIds.Owner || user.RoleId == UserRoleIds.Manager;

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
        var effectivePlanTypeId = subscription?.PlanTypeId ?? business!.PlanTypeId;
        var planType = PlanTypeIds.ToCode(effectivePlanTypeId);
        var token = GenerateToken(user, business!, branchId, branches, expiration, planType);

        return new AuthResponse
        {
            Token = token,
            RoleId = user.RoleId,
            Name = user.Name,
            BusinessId = user.BusinessId,
            CurrentBranchId = branchId,
            Branches = branches,
            PlanTypeId = effectivePlanTypeId,
            BusinessTypeId = business!.BusinessTypeId,
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

        // Resolve PlanType — default to Free if not provided
        var planTypeId = request.PlanTypeId ?? PlanTypeIds.Free;

        // Paid plans get a 14-day trial; Free plan has no trial
        var isPaidPlan = planTypeId is PlanTypeIds.Basic or PlanTypeIds.Pro or PlanTypeIds.Enterprise;
        DateTime? trialEndsAt = isPaidPlan ? DateTime.UtcNow.AddDays(14) : null;

        // ── Resolve giro IDs: prefer BusinessTypeIds array, fallback to single BusinessTypeId ──
        var giroIds = request.BusinessTypeIds?.Where(id => id > 0).Distinct().ToList();
        if (giroIds == null || giroIds.Count == 0)
        {
            giroIds = new List<int> { request.BusinessTypeId ?? BusinessTypeIds.General };
        }

        // Lookup catalog entries to derive HasKitchen / HasTables
        var allCatalogs = await _unitOfWork.Catalog.GetBusinessTypesAsync();
        var matchedCatalogs = allCatalogs.Where(c => giroIds.Contains(c.Id)).ToList();

        var hasKitchen = matchedCatalogs.Any(c => c.HasKitchen);
        var hasTables = matchedCatalogs.Any(c => c.HasTables);

        // Primary = first ID the user selected (preserve user's intent, not DB order)
        var primaryBusinessTypeId = matchedCatalogs.Any()
            ? giroIds.First(id => matchedCatalogs.Any(c => c.Id == id))
            : BusinessTypeIds.General;

        // ── Build entire entity graph with navigation properties ──
        var business = new Business
        {
            Name = request.BusinessName,
            BusinessTypeId = primaryBusinessTypeId,
            PlanTypeId = planTypeId,
            CountryCode = request.CountryCode ?? "MX",
            TrialEndsAt = trialEndsAt,
            TrialUsed = false,
            OnboardingStatusId = 1,
            CurrentOnboardingStep = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Build BusinessGiro junction rows
        foreach (var catalog in matchedCatalogs)
        {
            business.BusinessGiros.Add(new BusinessGiro
            {
                Business = business,
                BusinessTypeId = catalog.Id,
                CustomDescription = string.Equals(catalog.Code, "General", StringComparison.OrdinalIgnoreCase)
                    ? request.CustomGiroDescription
                    : null
            });
        }

        var branch = new Branch
        {
            Business = business,
            Name = $"{request.BusinessName} Principal",
            IsMatrix = true,
            IsActive = true,
            HasKitchen = hasKitchen,
            HasTables = hasTables,
            FolioPrefix = request.FolioPrefix,
            FolioCounter = 0,
            CreatedAt = DateTime.UtcNow
        };

        var user = new User
        {
            Business = business,
            Name = request.OwnerName,
            Email = request.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            RoleId = UserRoleIds.Owner,
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
        var zones = BuildDefaultZones(branch, primaryBusinessTypeId);

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
                    TableStatusId = TableStatusIds.Available,
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
        var token = GenerateToken(user, business, branch.Id, branches, TimeSpan.FromDays(_jwtSettings.OwnerExpirationDays), PlanTypeIds.ToCode(planTypeId));

        // Fire-and-forget: welcome email — never blocks the response
        _ = _emailService.SendWelcomeEmailAsync(request.Email, request.OwnerName, request.BusinessName);

        return new AuthResponse
        {
            Token = token,
            RoleId = user.RoleId,
            Name = user.Name,
            BusinessId = business.Id,
            CurrentBranchId = branch.Id,
            Branches = branches,
            PlanTypeId = business.PlanTypeId,
            BusinessTypeId = business.BusinessTypeId,
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

        if (user.RoleId == UserRoleIds.Owner)
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
            new(ClaimTypes.Role, UserRoleIds.ToCode(user.RoleId)),
            new(ClaimTypes.Name, user.Name),
            new("businessId", user.BusinessId.ToString()),
            new("branchId", branchId.ToString()),
            new("branches", branchesJson),
            new("planType", planType),
            new("businessType", BusinessTypeIds.ToCode(business.BusinessTypeId)),
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
    private static List<Zone> BuildDefaultZones(Branch branch, int businessTypeId)
    {
        if (businessTypeId is BusinessTypeIds.Retail or BusinessTypeIds.FoodTruck or BusinessTypeIds.General
            or BusinessTypeIds.Abarrotes or BusinessTypeIds.Ferreteria or BusinessTypeIds.Papeleria
            or BusinessTypeIds.Farmacia or BusinessTypeIds.Servicios)
            return [];

        var zones = new List<Zone>
        {
            new() { Branch = branch, Name = "Salón", Type = ZoneType.Salon, SortOrder = 1, IsActive = true }
        };

        if (businessTypeId == BusinessTypeIds.Bar)
            zones.Add(new Zone { Branch = branch, Name = "Barra", Type = ZoneType.BarSeats, SortOrder = 2, IsActive = true });

        zones.Add(new Zone { Branch = branch, Name = "Terraza", Type = ZoneType.Other, SortOrder = 3, IsActive = false });

        return zones;
    }

    #endregion
}
