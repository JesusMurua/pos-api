using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class ProductService : IProductService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFeatureGateService _featureGate;

    public ProductService(IUnitOfWork unitOfWork, IFeatureGateService featureGate)
    {
        _unitOfWork = unitOfWork;
        _featureGate = featureGate;
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
        await EnforcePlanProductLimitAsync(product.BranchId);
        await ValidateBarcodeUniqueAsync(product.BranchId, product.Barcode, null);
        await _unitOfWork.Products.AddAsync(product);
        await _unitOfWork.SaveChangesAsync();
        return product;
    }

    /// <summary>
    /// Updates an existing product.
    /// </summary>
    public async Task<Product> UpdateAsync(int id, Product product)
    {
        var existing = await _unitOfWork.Products.GetByIdWithRelationsAsync(id);

        if (existing == null)
            throw new NotFoundException($"Product with id {id} not found");

        await ValidateBarcodeUniqueAsync(existing.BranchId, product.Barcode, id);

        existing.Name = product.Name;
        existing.PriceCents = product.PriceCents;
        existing.ImageUrl = product.ImageUrl;
        existing.Description = product.Description;
        existing.Barcode = product.Barcode;
        existing.IsAvailable = product.IsAvailable;
        existing.IsPopular = product.IsPopular;
        existing.CategoryId = product.CategoryId;
        existing.TrackStock = product.TrackStock;
        existing.CurrentStock = product.CurrentStock;
        existing.LowStockThreshold = product.LowStockThreshold;

        existing.Sizes.Clear();
        foreach (var size in product.Sizes ?? [])
        {
            existing.Sizes.Add(new ProductSize
            {
                Label = size.Label,
                ExtraPriceCents = size.ExtraPriceCents
            });
        }

        existing.Extras.Clear();
        foreach (var extra in product.Extras ?? [])
        {
            existing.Extras.Add(new ProductExtra
            {
                Label = extra.Label,
                PriceCents = extra.PriceCents
            });
        }

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

    public async Task AddImageAsync(int productId, ProductImage image)
    {
        image.ProductId = productId;
        await _unitOfWork.Products.AddImageAsync(image);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<ProductImage?> GetImageAsync(int imageId)
    {
        return await _unitOfWork.Products.GetImageByIdAsync(imageId);
    }

    public async Task DeleteImageAsync(int imageId)
    {
        var image = await _unitOfWork.Products.GetImageByIdAsync(imageId);
        if (image != null)
        {
            _unitOfWork.Products.DeleteImage(image);
            await _unitOfWork.SaveChangesAsync();
        }
    }

    public async Task UpdateImageUrlAsync(int productId, string url)
    {
        var product = await _unitOfWork.Products.GetByIdAsync(productId);
        if (product != null)
        {
            product.ImageUrl = url;
            _unitOfWork.Products.Update(product);
            await _unitOfWork.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Gets an available product by barcode within a branch.
    /// </summary>
    public async Task<Product?> GetByBarcodeAsync(int branchId, string barcode)
    {
        return await _unitOfWork.Products.GetByBarcodeAsync(branchId, barcode);
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Delegates quantitative enforcement to the feature gate service.
    /// Soft enforcement: product counts are scoped to the branch that owns the new product.
    /// </summary>
    private async Task EnforcePlanProductLimitAsync(int branchId)
    {
        var branch = await _unitOfWork.Branches.GetByIdAsync(branchId);
        if (branch == null) return;

        var products = await _unitOfWork.Products.GetAsync(p => p.BranchId == branchId);
        await _featureGate.EnforceAsync(branch.BusinessId, FeatureKey.MaxProducts, products.Count());
    }

    private async Task ValidateBarcodeUniqueAsync(int branchId, string? barcode, int? excludeProductId)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return;

        var existing = await _unitOfWork.Products.GetByBarcodeAsync(branchId, barcode);
        if (existing != null && existing.Id != excludeProductId)
            throw new ValidationException("Este código de barras ya está registrado en otro producto");
    }

    #endregion
}
