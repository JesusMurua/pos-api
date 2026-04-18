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

    /// <summary>
    /// Replaces the macro category, custom description and full sub-giro set for a business.
    /// Deletes all existing <see cref="BusinessGiro"/> rows and inserts the new ones.
    /// </summary>
    Task<Business> UpdateGiroAsync(int businessId, int primaryMacroCategoryId, IReadOnlyList<int> businessTypeIds, string? customGiroDescription);

    /// <summary>
    /// Returns the business's current macro category, sub-giro set and optional custom description.
    /// Used by the frontend to rehydrate onboarding state after a reload or "back" navigation.
    /// </summary>
    Task<BusinessGiroResponse> GetGiroAsync(int businessId);
}

/// <summary>
/// Current giro configuration for a business — shape returned by GET /api/business/giro.
/// </summary>
public class BusinessGiroResponse
{
    public int PrimaryMacroCategoryId { get; set; }
    public List<int> BusinessTypeIds { get; set; } = new();
    public string? CustomGiroDescription { get; set; }
}
