using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing businesses.
/// </summary>
public interface IBusinessService
{
    /// <summary>
    /// Retrieves a business by its identifier.
    /// </summary>
    Task<Business> GetByIdAsync(int id);

    /// <summary>
    /// Creates a new business with its matrix branch and assigns the owner to it.
    /// </summary>
    /// <param name="business">The business data.</param>
    /// <param name="ownerUserId">The owner user ID to assign to the matrix branch.</param>
    Task<Business> CreateAsync(Business business, int ownerUserId);

    /// <summary>
    /// Updates an existing business.
    /// </summary>
    Task<Business> UpdateAsync(Business business);
}
