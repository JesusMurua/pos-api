using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing products.
/// </summary>
public interface IProductService
{
    /// <summary>
    /// Retrieves all active products for a branch, including sizes and extras.
    /// </summary>
    Task<IEnumerable<Product>> GetAllActiveAsync(int branchId);

    /// <summary>
    /// Retrieves a product by its identifier.
    /// </summary>
    Task<Product> GetByIdAsync(int id);

    /// <summary>
    /// Creates a new product.
    /// </summary>
    Task<Product> CreateAsync(Product product);

    /// <summary>
    /// Updates an existing product.
    /// </summary>
    Task<Product> UpdateAsync(int id, Product product);

    /// <summary>
    /// Toggles the active/inactive status of a product.
    /// </summary>
    Task<Product> ToggleActiveAsync(int id);

    Task AddImageAsync(int productId, ProductImage image);
    Task<ProductImage?> GetImageAsync(int imageId);
    Task DeleteImageAsync(int imageId);
    Task UpdateImageUrlAsync(int productId, string url);
}
