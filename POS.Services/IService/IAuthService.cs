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
}

public class AuthResponse
{
    public string Token { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Name { get; set; } = null!;
    public int? BranchId { get; set; }
    public int BusinessId { get; set; }
}
