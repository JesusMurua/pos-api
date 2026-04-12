using POS.Domain.DTOs.Product;
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
    public async Task<IEnumerable<ProductResponse>> GetAllActiveAsync(int branchId)
    {
        var categories = await _unitOfWork.Categories.GetActiveBranchCategoriesAsync(branchId);
        var responses = new List<ProductResponse>();

        foreach (var category in categories)
        {
            var categoryProducts = await _unitOfWork.Products.GetActiveWithExtrasAsync(category.Id);
            responses.AddRange(categoryProducts.Select(p => p.ToResponse()));
        }

        return responses;
    }

    /// <summary>
    /// Retrieves a product by its identifier with sizes and extras.
    /// </summary>
    public async Task<ProductResponse> GetByIdAsync(int id)
    {
        var product = await LoadWithRelationsOrThrowAsync(id);
        return product.ToResponse();
    }

    /// <summary>
    /// Creates a new product.
    /// </summary>
    public async Task<ProductResponse> CreateAsync(ProductRequest request)
    {
        await EnforcePlanProductLimitAsync(request.BranchId);
        await ValidateBarcodeUniqueAsync(request.BranchId, request.Barcode, null);

        var entity = request.ToEntity();
        await _unitOfWork.Products.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        return entity.ToResponse();
    }

    /// <summary>
    /// Updates an existing product.
    /// </summary>
    public async Task<ProductResponse> UpdateAsync(int id, ProductRequest request)
    {
        var existing = await LoadWithRelationsOrThrowAsync(id);

        await ValidateBarcodeUniqueAsync(existing.BranchId, request.Barcode, id);

        request.ApplyTo(existing);

        existing.Sizes ??= new List<ProductSize>();
        existing.Sizes.Clear();
        foreach (var size in request.Sizes)
        {
            existing.Sizes.Add(new ProductSize
            {
                Label = size.Label,
                ExtraPriceCents = size.ExtraPriceCents
            });
        }

        // Until the frontend sends grouped payloads, every update collapses
        // the existing group structure and rebuilds a single default group
        // from the flat request. Cascade delete cleans up orphaned extras.
        existing.ModifierGroups ??= new List<ProductModifierGroup>();
        existing.ModifierGroups.Clear();
        foreach (var group in ProductMapping.BuildDefaultGroups(request.Extras))
        {
            existing.ModifierGroups.Add(group);
        }

        _unitOfWork.Products.Update(existing);
        await _unitOfWork.SaveChangesAsync();

        return existing.ToResponse();
    }

    /// <summary>
    /// Toggles the active/inactive status of a product.
    /// </summary>
    public async Task<ProductResponse> ToggleActiveAsync(int id)
    {
        var product = await _unitOfWork.Products.GetByIdAsync(id);

        if (product == null)
            throw new NotFoundException($"Product with id {id} not found");

        product.IsAvailable = !product.IsAvailable;
        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveChangesAsync();

        return product.ToResponse();
    }

    /// <summary>
    /// Applies a stock movement to a tracked product. Mirrors the legacy
    /// controller logic: "in" adds, "out" subtracts (floored at 0),
    /// "adjustment" sets the exact value. IsAvailable follows threshold.
    /// </summary>
    public async Task<ProductResponse> UpdateStockAsync(int id, string type, decimal quantity)
    {
        var product = await LoadWithRelationsOrThrowAsync(id);

        if (!product.TrackStock)
            throw new ValidationException("Product does not have stock tracking enabled");

        var normalized = type?.ToLowerInvariant();
        switch (normalized)
        {
            case "in":
                product.CurrentStock += quantity;
                break;
            case "out":
                product.CurrentStock -= quantity;
                if (product.CurrentStock < 0) product.CurrentStock = 0;
                break;
            case "adjustment":
                product.CurrentStock = quantity;
                break;
            default:
                throw new ValidationException("Type must be 'in', 'out', or 'adjustment'");
        }

        if (product.CurrentStock > product.LowStockThreshold)
            product.IsAvailable = true;
        if (product.CurrentStock <= 0)
            product.IsAvailable = false;

        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveChangesAsync();

        return product.ToResponse();
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
    public async Task<ProductResponse?> GetByBarcodeAsync(int branchId, string barcode)
    {
        var product = await _unitOfWork.Products.GetByBarcodeAsync(branchId, barcode);
        return product?.ToResponse();
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Loads a product with its Sizes/Extras/Images populated or throws
    /// <see cref="NotFoundException"/>. Centralised so all entity-side
    /// reads in the service go through the same include list.
    /// </summary>
    private async Task<Product> LoadWithRelationsOrThrowAsync(int id)
    {
        var product = await _unitOfWork.Products.GetByIdWithRelationsAsync(id);

        if (product == null)
            throw new NotFoundException($"Product with id {id} not found");

        return product;
    }

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
