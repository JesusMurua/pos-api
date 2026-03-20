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
    /// Creates a new business.
    /// </summary>
    Task<Business> CreateAsync(Business business);
}
