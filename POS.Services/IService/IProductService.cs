using POS.Domain.DTOs.Product;
using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing products. Accepts and returns DTOs so
/// the Entity Framework model never leaks through the API boundary.
/// </summary>
public interface IProductService
{
    /// <summary>
    /// Retrieves all active products for a branch, including sizes and extras.
    /// </summary>
    Task<IEnumerable<ProductResponse>> GetAllActiveAsync(int branchId);

    /// <summary>
    /// Retrieves a product by its identifier.
    /// </summary>
    Task<ProductResponse> GetByIdAsync(int id);

    /// <summary>
    /// Creates a new product from the given request payload.
    /// </summary>
    Task<ProductResponse> CreateAsync(ProductRequest request);

    /// <summary>
    /// Updates an existing product with the given request payload.
    /// </summary>
    Task<ProductResponse> UpdateAsync(int id, ProductRequest request);

    /// <summary>
    /// Toggles the active/inactive status of a product.
    /// </summary>
    Task<ProductResponse> ToggleActiveAsync(int id);

    /// <summary>
    /// Applies a stock movement (in / out / adjustment) to a tracked product.
    /// Pushes the previously-inlined controller logic into the service layer
    /// so the Product entity no longer needs to leak through the API.
    /// </summary>
    Task<ProductResponse> UpdateStockAsync(int id, string type, decimal quantity);

    Task<ProductResponse?> GetByBarcodeAsync(int branchId, string barcode);

    Task AddImageAsync(int productId, ProductImage image);
    Task<ProductImage?> GetImageAsync(int imageId);
    Task DeleteImageAsync(int imageId);
    Task UpdateImageUrlAsync(int productId, string url);
}
