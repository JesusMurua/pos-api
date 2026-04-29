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
using POS.Domain.Models.Catalogs;
using POS.Domain.Settings;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class AuthService : IAuthService
{
    private const string SessionTypeEmail = "email";
    private const string SessionTypePin = "pin";

    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtSettings _jwtSettings;
    private readonly IEmailService _emailService;
    private readonly IFeatureGateService _featureGate;
    private readonly ITenantSeedingService _tenantSeedingService;

    public AuthService(
        IUnitOfWork unitOfWork,
        IOptions<JwtSettings> jwtSettings,
        IEmailService emailService,
        IFeatureGateService featureGate,
        ITenantSeedingService tenantSeedingService)
    {
        _unitOfWork = unitOfWork;
        _jwtSettings = jwtSettings.Value;
        _emailService = emailService;
        _featureGate = featureGate;
        _tenantSeedingService = tenantSeedingService;
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

        // Single Source of Truth: Business.PlanTypeId is kept in sync by the Stripe worker.
        var subscription = await ResolveSubscriptionAsync(user.BusinessId);
        var planType = PlanTypeIds.ToCode(business!.PlanTypeId);
        var macroCode = await ResolveMacroCodeAsync(business.PrimaryMacroCategoryId);
        var features = await _featureGate.GetEnabledFeaturesAsync(business.Id);
        var token = GenerateToken(user, business, currentBranchId, branches, TimeSpan.FromDays(_jwtSettings.OwnerExpirationDays), planType, macroCode, features, SessionTypeEmail);

        return BuildAuthResponse(token, user, business, currentBranchId, branches, subscription);
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
        var planType = PlanTypeIds.ToCode(business!.PlanTypeId);
        var macroCode = await ResolveMacroCodeAsync(business.PrimaryMacroCategoryId);
        var features = await _featureGate.GetEnabledFeaturesAsync(business.Id);
        var token = GenerateToken(matchedUser, business, currentBranchId, branches, TimeSpan.FromHours(_jwtSettings.PinExpirationHours), planType, macroCode, features, SessionTypePin);

        return BuildAuthResponse(token, matchedUser, business, currentBranchId, branches, subscription);
    }

    /// <summary>
    /// Switches the authenticated user to a different branch and returns a new JWT.
    /// </summary>
    public async Task<AuthResponse> SwitchBranchAsync(int userId, int branchId, string? sessionType)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || !user.IsActive)
            throw new NotFoundException($"User with id {userId} not found");

        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId);
        if (branch == null || !branch.IsActive)
            throw new NotFoundException($"Branch with id {branchId} not found");

        if (branch.BusinessId != user.BusinessId)
            throw new UnauthorizedException("Branch does not belong to the user's business");

        if (!UserRoleIds.IsAdminRole(user.RoleId))
        {
            var userBranches = await _unitOfWork.UserBranches.GetByUserIdAsync(userId);
            if (!userBranches.Any(ub => ub.BranchId == branchId))
                throw new UnauthorizedException("User is not assigned to the requested branch");
        }

        var business = await _unitOfWork.Business.GetByIdAsync(user.BusinessId);
        var (_, branches) = await ResolveBranchesAsync(user);

        var validatedSessionType = ValidateSessionType(sessionType);
        var expiration = ResolveExpiration(validatedSessionType);

        var subscription = await ResolveSubscriptionAsync(user.BusinessId);
        var planType = PlanTypeIds.ToCode(business!.PlanTypeId);
        var macroCode = await ResolveMacroCodeAsync(business.PrimaryMacroCategoryId);
        var features = await _featureGate.GetEnabledFeaturesAsync(business.Id);
        var token = GenerateToken(user, business, branchId, branches, expiration, planType, macroCode, features, validatedSessionType);

        return BuildAuthResponse(token, user, business, branchId, branches, subscription);
    }

    /// <summary>
    /// Rehydrates the current session from the database and returns a freshly minted
    /// JWT plus an up-to-date <see cref="AuthResponse"/>. The new token inherits the
    /// same <paramref name="sessionType"/> as the incoming token.
    /// </summary>
    public async Task<AuthResponse> GetSessionAsync(int userId, string? sessionType)
    {
        // Validate sessionType up-front so legacy or tampered tokens are rejected
        // before we spend DB round-trips on rehydrating a session we will not issue.
        var validatedSessionType = ValidateSessionType(sessionType);

        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || !user.IsActive)
            throw new UnauthorizedException("User is no longer active");

        var business = await _unitOfWork.Business.GetByIdAsync(user.BusinessId);
        if (business == null || !business.IsActive)
            throw new UnauthorizedException("Business is no longer active");

        var (currentBranchId, branches) = await ResolveBranchesAsync(user);

        var expiration = ResolveExpiration(validatedSessionType);

        var subscription = await ResolveSubscriptionAsync(user.BusinessId);
        var planType = PlanTypeIds.ToCode(business.PlanTypeId);
        var macroCode = await ResolveMacroCodeAsync(business.PrimaryMacroCategoryId);
        var features = await _featureGate.GetEnabledFeaturesAsync(business.Id);
        var token = GenerateToken(user, business, currentBranchId, branches, expiration, planType, macroCode, features, validatedSessionType);

        return BuildAuthResponse(token, user, business, currentBranchId, branches, subscription);
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

        // Resolve macro category — sub-giros and CustomGiroDescription are captured
        // later via PUT /api/business/giro once the user reaches onboarding step 2.
        var macros = await _unitOfWork.Catalog.GetMacroCategoriesAsync();
        var primaryMacro = macros.FirstOrDefault(m => m.Id == request.PrimaryMacroCategoryId)
            ?? throw new ValidationException("PrimaryMacroCategoryId inválido");

        // Resolve timezone. Empty/null → column default. Non-empty but unresolvable
        // → reject so the DB never holds an opaque string that will silently
        // fall back in every per-day query.
        var resolvedTimeZoneId = ResolveTimeZoneId(request.TimeZoneId);

        // ── Build entire entity graph with navigation properties ──
        var business = new Business
        {
            Name = request.BusinessName,
            PrimaryMacroCategoryId = primaryMacro.Id,
            PlanTypeId = planTypeId,
            CountryCode = request.CountryCode ?? "MX",
            TrialEndsAt = trialEndsAt,
            TrialUsed = false,
            OnboardingStatusId = 1,
            CurrentOnboardingStep = 1,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var branch = new Branch
        {
            Business = business,
            Name = $"{request.BusinessName} Principal",
            IsMatrix = true,
            IsActive = true,
            HasKitchen = primaryMacro.HasKitchen,
            HasTables = primaryMacro.HasTables,
            FolioPrefix = request.FolioPrefix,
            FolioCounter = 0,
            TimeZoneId = resolvedTimeZoneId,
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
        var zones = BuildDefaultZones(branch, primaryMacro);

        // ── Atomic transaction: register graph + macro-shaped seed data ──
        await using var transaction = await _unitOfWork.BeginTransactionAsync();

        try
        {
            await _unitOfWork.Business.AddAsync(business);
            await _unitOfWork.Branches.AddAsync(branch);
            await _unitOfWork.Users.AddAsync(user);
            await _unitOfWork.UserBranches.AddAsync(userBranch);

            if (zones.Count > 0)
                await _unitOfWork.Zones.AddRangeAsync(zones);

            // Default table so table-service businesses have at least one
            if (primaryMacro.HasTables)
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

            // Seed default categories + sample products for the macro so the operator
            // sees a populated POS on first login. Runs inside the same transaction —
            // any seeding failure rolls the entire registration back.
            await _tenantSeedingService.SeedDefaultsForMacroAsync(branch.Id, primaryMacro.Id);

            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            throw new ValidationException("EMAIL_ALREADY_EXISTS");
        }

        // Generate JWT — resolve features once the transaction has committed so the
        // gate service sees the fresh BusinessGiros rows.
        var branches = new List<BranchSummary> { new() { Id = branch.Id, Name = branch.Name } };
        var features = await _featureGate.GetEnabledFeaturesAsync(business.Id);
        var token = GenerateToken(user, business, branch.Id, branches, TimeSpan.FromDays(_jwtSettings.OwnerExpirationDays), PlanTypeIds.ToCode(planTypeId), primaryMacro.InternalCode, features, SessionTypeEmail);

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
            PrimaryMacroCategoryId = business.PrimaryMacroCategoryId,
            TrialEndsAt = business.TrialEndsAt?.ToString("o"),
            OnboardingCompleted = business.OnboardingCompleted,
            CurrentOnboardingStep = business.CurrentOnboardingStep,
            OnboardingStatusId = business.OnboardingStatusId
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

    private async Task<string> ResolveMacroCodeAsync(int primaryMacroCategoryId)
    {
        var macros = await _unitOfWork.Catalog.GetMacroCategoriesAsync();
        return macros.FirstOrDefault(m => m.Id == primaryMacroCategoryId)?.InternalCode ?? string.Empty;
    }

    public string GenerateDeviceToken(Device device, Business business, string macroCode, IReadOnlyList<string> features)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var featuresJson = JsonSerializer.Serialize(features);

        // Device tokens intentionally omit userId/roleId so that infrastructure screens
        // cannot impersonate human operators on HTTP endpoints that enforce role checks.
        // macroCode is supplied by the caller — DO NOT read it from
        // business.PrimaryMacroCategory. That nav is not eager-loaded by the
        // BusinessRepository or DeviceActivationCodeRepository, and the resulting
        // empty claim was the root cause of the "Gym branch loads Restaurant UI" bug.
        var claims = new List<Claim>
        {
            new("type", "device"),
            new("deviceId", device.Id.ToString()),
            new("businessId", business.Id.ToString()),
            new("branchId", device.BranchId.ToString()),
            new("mode", device.Mode),
            new("planType", PlanTypeIds.ToCode(business.PlanTypeId)),
            new("macroCategory", macroCode),
            new("features", featuresJson)
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddYears(10),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private string GenerateToken(
        User user,
        Business business,
        int branchId,
        List<BranchSummary> branches,
        TimeSpan expiration,
        string planType,
        string macroCategoryCode,
        IReadOnlyList<string> features,
        string sessionType)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var branchesJson = JsonSerializer.Serialize(
            branches.Select(b => new { id = b.Id, name = b.Name }));

        var featuresJson = JsonSerializer.Serialize(features);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Role, UserRoleIds.ToCode(user.RoleId)),
            new(ClaimTypes.Name, user.Name),
            new("businessId", user.BusinessId.ToString()),
            new("branchId", branchId.ToString()),
            new("branches", branchesJson),
            new("planType", planType),
            new("macroCategory", macroCategoryCode),
            new("trialEndsAt", business.TrialEndsAt?.ToString("o") ?? ""),
            new("onboardingCompleted", business.OnboardingCompleted.ToString().ToLower()),
            new("features", featuresJson),
            new("sessionType", sessionType)
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

    /// <summary>
    /// Strictly validates <paramref name="sessionType"/> against the canonical
    /// whitelist (<c>email</c> / <c>pin</c>). Any other value — including null,
    /// empty, or unknown — raises <see cref="UnauthorizedException"/> so legacy
    /// or forged tokens without a valid session-type claim are rejected at the
    /// service boundary. No graceful-fallback path exists.
    /// </summary>
    /// <exception cref="UnauthorizedException">
    /// Thrown when the claim is missing or not one of the allowed values.
    /// </exception>
    private static string ValidateSessionType(string? sessionType) => sessionType switch
    {
        SessionTypeEmail => SessionTypeEmail,
        SessionTypePin => SessionTypePin,
        _ => throw new UnauthorizedException("Missing or invalid sessionType claim")
    };

    /// <summary>
    /// Resolves token lifetime from <paramref name="sessionType"/>. PIN sessions
    /// are always bounded to <c>PinExpirationHours</c> even when the underlying
    /// user has an admin role — a PIN session cannot upgrade itself.
    /// </summary>
    private TimeSpan ResolveExpiration(string sessionType) => sessionType == SessionTypePin
        ? TimeSpan.FromHours(_jwtSettings.PinExpirationHours)
        : TimeSpan.FromDays(_jwtSettings.OwnerExpirationDays);

    /// <summary>
    /// Builds the outgoing <see cref="AuthResponse"/> from a freshly issued token
    /// and the resolved user / business / subscription graph. Centralizes DTO
    /// population so every login flow surfaces the same set of fields.
    /// </summary>
    private static AuthResponse BuildAuthResponse(
        string token,
        User user,
        Business business,
        int currentBranchId,
        List<BranchSummary> branches,
        Subscription? subscription) => new()
    {
        Token = token,
        RoleId = user.RoleId,
        Name = user.Name,
        BusinessId = user.BusinessId,
        CurrentBranchId = currentBranchId,
        Branches = branches,
        PlanTypeId = business.PlanTypeId,
        PrimaryMacroCategoryId = business.PrimaryMacroCategoryId,
        TrialEndsAt = subscription?.TrialEndsAt?.ToString("o"),
        SubscriptionStatus = subscription?.Status,
        OnboardingCompleted = business.OnboardingCompleted,
        CurrentOnboardingStep = business.CurrentOnboardingStep,
        OnboardingStatusId = business.OnboardingStatusId
    };

    /// <summary>
    /// Normalizes and validates an incoming IANA timezone identifier. Null or
    /// empty collapses to the default (<c>America/Mexico_City</c>); any other
    /// value must resolve via <see cref="TimeZoneInfo.FindSystemTimeZoneById"/>
    /// or the call raises <see cref="ValidationException"/> (VR-001).
    /// </summary>
    private static string ResolveTimeZoneId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return TimeZoneHelper.DefaultTimeZone;

        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(raw);
            return raw;
        }
        catch
        {
            throw new ValidationException(
                "TimeZoneId is not a valid IANA timezone identifier");
        }
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
    /// Only table-centric macros get salon/terrace/bar zones; other macros start empty.
    /// </summary>
    private static List<Zone> BuildDefaultZones(Branch branch, MacroCategory macro)
    {
        if (!macro.HasTables)
            return [];

        return new List<Zone>
        {
            new() { Branch = branch, Name = "Salón", Type = ZoneType.Salon, SortOrder = 1, IsActive = true },
            new() { Branch = branch, Name = "Barra", Type = ZoneType.BarSeats, SortOrder = 2, IsActive = true },
            new() { Branch = branch, Name = "Terraza", Type = ZoneType.Other, SortOrder = 3, IsActive = false }
        };
    }

    #endregion
}
