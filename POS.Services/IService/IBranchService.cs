using POS.Domain.Models;
using POS.Domain.PartialModels;

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
    /// Retrieves all branches for a business.
    /// </summary>
    Task<IEnumerable<Branch>> GetByBusinessAsync(int businessId);

    /// <summary>
    /// Creates a new branch.
    /// </summary>
    Task<Branch> CreateAsync(Branch branch);

    /// <summary>
    /// Updates an existing branch's name and location, and optionally kitchen/tables settings.
    /// </summary>
    Task<Branch> UpdateAsync(int id, Branch branch, bool? hasKitchen = null, bool? hasTables = null);

    /// <summary>
    /// Copies the full catalog (categories, products, sizes, extras) from one branch to another.
    /// Skips if the target branch already has categories.
    /// </summary>
    Task<int> CopyCatalogAsync(int targetBranchId, int sourceBranchId);

    /// <summary>
    /// Retrieves a branch with its business configuration.
    /// </summary>
    Task<Branch> GetConfigAsync(int branchId);

    /// <summary>
    /// Retrieves a flat DTO with branch and business configuration.
    /// </summary>
    Task<BranchConfigDto> GetBranchConfigDtoAsync(int branchId);

    /// <summary>
    /// Updates the branch name.
    /// </summary>
    Task<Branch> UpdateConfigAsync(int branchId, string name);

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
