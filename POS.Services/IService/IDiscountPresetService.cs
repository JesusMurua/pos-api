using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing discount presets.
/// </summary>
public interface IDiscountPresetService
{
    /// <summary>
    /// Gets all active discount presets for a branch.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <returns>Active discount presets ordered by name.</returns>
    Task<IEnumerable<DiscountPreset>> GetByBranchAsync(int branchId);

    /// <summary>
    /// Creates a new discount preset.
    /// </summary>
    /// <param name="preset">The discount preset to create.</param>
    /// <returns>The created discount preset.</returns>
    Task<DiscountPreset> CreateAsync(DiscountPreset preset);

    /// <summary>
    /// Updates an existing discount preset.
    /// </summary>
    /// <param name="id">The preset identifier.</param>
    /// <param name="preset">The updated preset data.</param>
    /// <returns>The updated discount preset.</returns>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when preset is not found.</exception>
    Task<DiscountPreset> UpdateAsync(int id, DiscountPreset preset);

    /// <summary>
    /// Soft deletes a discount preset by setting IsActive to false.
    /// </summary>
    /// <param name="id">The preset identifier.</param>
    /// <returns>True if successfully deactivated.</returns>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when preset is not found.</exception>
    Task<bool> DeleteAsync(int id);
}
