using POS.Domain.DTOs.Category;
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
    /// Hard-deletes a category. See <see cref="ICategoryService.DeleteAsync"/>.
    /// </summary>
    public async Task<DeleteCategoryResult> DeleteAsync(int id)
    {
        // Load through the filtered DbSet so a foreign-branch id resolves to
        // NotFound rather than leaking a cross-tenant delete.
        var category = (await _unitOfWork.Categories.GetAsync(c => c.Id == id))
            .FirstOrDefault();
        if (category == null)
            return DeleteCategoryResult.NotFound();

        // Count ALL products in the category (not just available ones): a
        // cascade delete would drag every product — including sold ones with
        // fiscal history — so any attached product blocks the delete.
        var productCount = await _unitOfWork.Products.CountAsync(p => p.CategoryId == id);
        if (productCount > 0)
            return DeleteCategoryResult.HasProducts(productCount);

        _unitOfWork.Categories.Delete(category);
        await _unitOfWork.SaveChangesAsync();
        return DeleteCategoryResult.Deleted();
    }

    #endregion
}
