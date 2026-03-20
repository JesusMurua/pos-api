using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing branches and their configuration.
/// </summary>
public interface IBranchService
{
    /// <summary>
    /// Retrieves a branch by its identifier.
    /// </summary>
    Task<Branch> GetByIdAsync(int id);

    /// <summary>
    /// Retrieves a branch with its business configuration.
    /// </summary>
    Task<Branch> GetConfigAsync(int branchId);

    /// <summary>
    /// Updates the branch name and location.
    /// </summary>
    Task<Branch> UpdateConfigAsync(int branchId, string name, string? locationName);

    /// <summary>
    /// Verifies a PIN against the branch's stored hash.
    /// </summary>
    Task<bool> VerifyPinAsync(int branchId, string pin);

    /// <summary>
    /// Updates the branch PIN after verifying the current one.
    /// </summary>
    /// <exception cref="Domain.Exceptions.ValidationException">
    /// Thrown when the current PIN is incorrect.
    /// </exception>
    Task<bool> UpdatePinAsync(int branchId, string currentPin, string newPin);
}
