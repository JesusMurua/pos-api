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
}

/// <summary>
/// Request for public registration.
/// </summary>
public class RegisterRequest
{
    public string BusinessName { get; set; } = null!;
    public string OwnerName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;
    public string? BusinessType { get; set; }
}

/// <summary>
/// Response returned after successful authentication.
/// </summary>
public class AuthResponse
{
    public string Token { get; set; } = null!;
    public string Role { get; set; } = null!;
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
}

/// <summary>
/// Lightweight branch representation for login responses and JWT claims.
/// </summary>
public class BranchSummary
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
}
