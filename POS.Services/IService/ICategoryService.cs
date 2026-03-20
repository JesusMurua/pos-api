using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing categories.
/// </summary>
public interface ICategoryService
{
    /// <summary>
    /// Retrieves all active categories for a branch.
    /// </summary>
    Task<IEnumerable<Category>> GetAllActiveAsync(int branchId);

    /// <summary>
    /// Creates a new category.
    /// </summary>
    Task<Category> CreateAsync(Category category);

    /// <summary>
    /// Updates an existing category.
    /// </summary>
    Task<Category> UpdateAsync(int id, Category category);

    /// <summary>
    /// Toggles the active/inactive status of a category.
    /// </summary>
    Task<Category> ToggleActiveAsync(int id);

    /// <summary>
    /// Deletes a category if it has no active products.
    /// </summary>
    /// <exception cref="Domain.Exceptions.ValidationException">
    /// Thrown when the category has active products.
    /// </exception>
    Task<bool> DeleteAsync(int id);
}
