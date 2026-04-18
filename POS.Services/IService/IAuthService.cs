using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides authentication operations.
/// </summary>
public interface IAuthService
{
    /// <summary>
    /// Authenticates an owner by email and password. Returns a JWT token response.
    /// </summary>
    Task<AuthResponse> EmailLoginAsync(string email, string password);

    /// <summary>
    /// Authenticates a staff member by branch PIN. Returns a JWT token response.
    /// </summary>
    Task<AuthResponse> PinLoginAsync(int branchId, string pin);

    /// <summary>
    /// Switches the authenticated user to a different branch and returns a new JWT.
    /// Owner/Manager can switch to any branch in their business.
    /// Other roles can only switch to branches assigned via UserBranch.
    /// </summary>
    Task<AuthResponse> SwitchBranchAsync(int userId, int branchId);

    /// <summary>
    /// Registers a new business with owner account and matrix branch.
    /// Returns JWT token so user can immediately enter the app.
    /// </summary>
    Task<AuthResponse> RegisterAsync(RegisterRequest request);

    /// <summary>
    /// Issues a long-lived JWT that represents a physical device (KDS screen, kiosk)
    /// rather than a human user. The token carries <c>businessId</c>, <c>branchId</c>,
    /// <c>deviceId</c>, a <c>type=device</c> discriminator and the resolved feature
    /// matrix. It has no <c>userId</c> / <c>roleId</c> and its lifetime is measured
    /// in years so that infrastructure hardware does not require human re-auth.
    /// </summary>
    string GenerateDeviceToken(Device device, Business business, IReadOnlyList<string> features);
}

/// <summary>
/// Request for public registration. Only the macro category is captured at this stage —
/// sub-giros and <c>CustomGiroDescription</c> are assigned later during onboarding.
/// </summary>
public class RegisterRequest
{
    public string BusinessName { get; set; } = null!;
    public string OwnerName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;

    /// <summary>Macro category id (1-4) that drives POS experience and plan rules.</summary>
    public int PrimaryMacroCategoryId { get; set; }

    public int? PlanTypeId { get; set; }
    public string? FolioPrefix { get; set; }
    public string? CountryCode { get; set; }
}

/// <summary>
/// Response returned after successful authentication.
/// </summary>
public class AuthResponse
{
    public string Token { get; set; } = null!;
    public int RoleId { get; set; }
    public string Name { get; set; } = null!;
    public int BusinessId { get; set; }

    /// <summary>
    /// The branch ID used for the current session (included in the JWT).
    /// </summary>
    public int CurrentBranchId { get; set; }

    /// <summary>
    /// All branches the user has access to.
    /// </summary>
    public List<BranchSummary> Branches { get; set; } = new();

    public int PlanTypeId { get; set; }

    /// <summary>Drives POS experience, plan rules and Stripe pricing group.</summary>
    public int PrimaryMacroCategoryId { get; set; }

    public string? TrialEndsAt { get; set; }
    public string? SubscriptionStatus { get; set; }
    public bool OnboardingCompleted { get; set; }
}

/// <summary>
/// Lightweight branch representation for login responses and JWT claims.
/// </summary>
public class BranchSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}
