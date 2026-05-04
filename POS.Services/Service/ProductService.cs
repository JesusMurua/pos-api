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
    private readonly ITaxResolverService _taxResolver;

    public ProductService(
        IUnitOfWork unitOfWork,
        IFeatureGateService featureGate,
        ITaxResolverService taxResolver)
    {
        _unitOfWork = unitOfWork;
        _featureGate = featureGate;
        _taxResolver = taxResolver;
    }

    #region Public API Methods

    /// <summary>
    /// Retrieves all active products for a branch, grouped by category with sizes and extras.
    /// </summary>
    public async Task<IEnumerable<ProductResponse>> GetAllActiveAsync(int branchId)
    {
        var categories = await _unitOfWork.Categories.GetActiveBranchCategoriesAsync(branchId);
        var ctx = await LoadTaxContextAsync(branchId);
        var responses = new List<ProductResponse>();

        foreach (var category in categories)
        {
            var categoryProducts = await _unitOfWork.Products.GetActiveWithExtrasAsync(category.Id);
            responses.AddRange(categoryProducts.Select(p => MapWithEffectiveTax(p, ctx)));
        }

        return responses;
    }

    /// <summary>
    /// Retrieves a product by its identifier with sizes and extras.
    /// </summary>
    public async Task<ProductResponse> GetByIdAsync(int id)
    {
        var product = await LoadWithRelationsOrThrowAsync(id);
        var ctx = await LoadTaxContextAsync(product.BranchId);
        return MapWithEffectiveTax(product, ctx);
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

        var ctx = await LoadTaxContextAsync(entity.BranchId);
        return MapWithEffectiveTax(entity, ctx);
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

        // Wholesale replacement: clear the existing group tree and rebuild
        // it from the request. Cascade delete cleans up orphaned extras so
        // we don't have to chase the old rows by hand.
        existing.ModifierGroups ??= new List<ProductModifierGroup>();
        existing.ModifierGroups.Clear();
        foreach (var groupRequest in request.ModifierGroups)
        {
            existing.ModifierGroups.Add(groupRequest.ToEntity());
        }

        _unitOfWork.Products.Update(existing);
        await _unitOfWork.SaveChangesAsync();

        var ctx = await LoadTaxContextAsync(existing.BranchId);
        return MapWithEffectiveTax(existing, ctx);
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

        var ctx = await LoadTaxContextAsync(product.BranchId);
        return MapWithEffectiveTax(product, ctx);
    }

    /// <summary>
    /// Applies a stock movement to a tracked product. Mirrors the legacy
    /// controller logic: "in" adds, "out" subtracts (floored at 0),
    /// "adjustment" sets the exact value. IsAvailable follows threshold.
    /// </summary>
    public async Task<ProductResponse> UpdateStockAsync(int id, string type, decimal quantity)
    {
        var product = await LoadWithRelationsOrThrowAsync(id);
        var ctx = await LoadTaxContextAsync(product.BranchId);

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

        return MapWithEffectiveTax(product, ctx);
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
        if (product == null) return null;

        var ctx = await LoadTaxContextAsync(branchId);
        return MapWithEffectiveTax(product, ctx);
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

    /// <summary>
    /// Eager-loads the inputs the resolver needs once per request. Pass the
    /// returned context through to <see cref="MapWithEffectiveTax"/> so each
    /// product is resolved in-memory without extra round-trips.
    /// </summary>
    private async Task<TaxContext> LoadTaxContextAsync(int branchId)
    {
        var branch = (await _unitOfWork.Branches.GetAsync(
            b => b.Id == branchId, "Business.DefaultTax"))
            .FirstOrDefault()
            ?? throw new NotFoundException($"Branch with id {branchId} not found");

        var business = branch.Business
            ?? throw new ValidationException(
                $"Branch {branchId} is detached from any business — cannot resolve tax policy.");

        var countryDefaults = (await _unitOfWork.Taxes.GetAsync(
            t => t.CountryCode == business.CountryCode && t.IsDefault))
            .ToList();

        return new TaxContext(business, countryDefaults);
    }

    private ProductResponse MapWithEffectiveTax(Product product, TaxContext ctx)
    {
        var resolution = _taxResolver.ResolveTax(product, ctx.Business, ctx.CountryDefaults);
        var response = product.ToResponse();
        response.EffectiveTaxRate = resolution.Rate;
        response.EffectiveIsTaxIncluded = product.IsTaxIncluded;
        return response;
    }

    private sealed record TaxContext(Business Business, IReadOnlyList<Tax> CountryDefaults);

    #endregion
}
