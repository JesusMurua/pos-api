using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class CategoryService : ICategoryService
{
    private readonly IUnitOfWork _unitOfWork;

    public CategoryService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Retrieves all active categories for a branch, ordered by SortOrder.
    /// </summary>
    public async Task<IEnumerable<Category>> GetAllActiveAsync(int branchId)
    {
        return await _unitOfWork.Categories.GetActiveBranchCategoriesAsync(branchId);
    }

    /// <summary>
    /// Creates a new category.
    /// </summary>
    public async Task<Category> CreateAsync(Category category)
    {
        await _unitOfWork.Categories.AddAsync(category);
        await _unitOfWork.SaveChangesAsync();
        return category;
    }

    /// <summary>
    /// Updates an existing category.
    /// </summary>
    public async Task<Category> UpdateAsync(int id, Category category)
    {
        var existing = await _unitOfWork.Categories.GetByIdAsync(id);

        if (existing == null)
            throw new NotFoundException($"Category with id {id} not found");

        existing.Name = category.Name;
        existing.Icon = category.Icon;
        existing.SortOrder = category.SortOrder;

        _unitOfWork.Categories.Update(existing);
        await _unitOfWork.SaveChangesAsync();
        return existing;
    }

    /// <summary>
    /// Toggles the active/inactive status of a category.
    /// </summary>
    public async Task<Category> ToggleActiveAsync(int id)
    {
        var category = await _unitOfWork.Categories.GetByIdAsync(id);

        if (category == null)
            throw new NotFoundException($"Category with id {id} not found");

        category.IsActive = !category.IsActive;
        _unitOfWork.Categories.Update(category);
        await _unitOfWork.SaveChangesAsync();
        return category;
    }

    /// <summary>
    /// Deletes a category if it has no active products.
    /// </summary>
    public async Task<bool> DeleteAsync(int id)
    {
        var results = await _unitOfWork.Categories.GetAsync(
            c => c.Id == id,
            "Products");

        var category = results.FirstOrDefault();

        if (category == null)
            throw new NotFoundException($"Category with id {id} not found");

        var hasActiveProducts = category.Products?.Any(p => p.IsAvailable) ?? false;

        if (hasActiveProducts)
            throw new ValidationException("Cannot delete a category with active products");

        _unitOfWork.Categories.Delete(category);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    #endregion
}
