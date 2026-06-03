using POS.Domain.DTOs.Category;
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
    /// Hard-deletes a category, but only when it has no products attached
    /// (active or inactive). A category with any product resolves to
    /// <see cref="DeleteCategoryOutcome.HasProducts"/> so a cascade delete can
    /// never silently remove products that carry order/fiscal history. Branch
    /// scoping is enforced by the global query filter, so a category outside
    /// the caller's branch resolves to <see cref="DeleteCategoryOutcome.NotFound"/>.
    /// </summary>
    Task<DeleteCategoryResult> DeleteAsync(int id);
}
