using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class ProductService : IProductService
{
    private readonly IUnitOfWork _unitOfWork;

    public ProductService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    /// <summary>
    /// Retrieves all active products for a branch, grouped by category with sizes and extras.
    /// </summary>
    public async Task<IEnumerable<Product>> GetAllActiveAsync(int branchId)
    {
        var categories = await _unitOfWork.Categories.GetActiveBranchCategoriesAsync(branchId);
        var products = new List<Product>();

        foreach (var category in categories)
        {
            var categoryProducts = await _unitOfWork.Products.GetActiveWithExtrasAsync(category.Id);
            products.AddRange(categoryProducts);
        }

        return products;
    }

    /// <summary>
    /// Retrieves a product by its identifier with sizes and extras.
    /// </summary>
    public async Task<Product> GetByIdAsync(int id)
    {
        var results = await _unitOfWork.Products.GetAsync(
            p => p.Id == id,
            "Sizes,Extras");

        var product = results.FirstOrDefault();

        if (product == null)
            throw new NotFoundException($"Product with id {id} not found");

        return product;
    }

    /// <summary>
    /// Creates a new product.
    /// </summary>
    public async Task<Product> CreateAsync(Product product)
    {
        await _unitOfWork.Products.AddAsync(product);
        await _unitOfWork.SaveChangesAsync();
        return product;
    }

    /// <summary>
    /// Updates an existing product.
    /// </summary>
    public async Task<Product> UpdateAsync(int id, Product product)
    {
        var existing = await _unitOfWork.Products.GetByIdAsync(id);

        if (existing == null)
            throw new NotFoundException($"Product with id {id} not found");

        existing.Name = product.Name;
        existing.PriceCents = product.PriceCents;
        existing.ImageUrl = product.ImageUrl;
        existing.IsAvailable = product.IsAvailable;
        existing.IsPopular = product.IsPopular;
        existing.CategoryId = product.CategoryId;
        existing.TrackStock = product.TrackStock;
        existing.CurrentStock = product.CurrentStock;
        existing.LowStockThreshold = product.LowStockThreshold;

        _unitOfWork.Products.Update(existing);
        await _unitOfWork.SaveChangesAsync();
        return existing;
    }

    /// <summary>
    /// Toggles the active/inactive status of a product.
    /// </summary>
    public async Task<Product> ToggleActiveAsync(int id)
    {
        var product = await _unitOfWork.Products.GetByIdAsync(id);

        if (product == null)
            throw new NotFoundException($"Product with id {id} not found");

        product.IsAvailable = !product.IsAvailable;
        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveChangesAsync();
        return product;
    }

    #endregion
}
