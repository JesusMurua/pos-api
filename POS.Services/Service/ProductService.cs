using Microsoft.EntityFrameworkCore;
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
    private readonly IStorageService _storageService;

    public ProductService(
        IUnitOfWork unitOfWork,
        IFeatureGateService featureGate,
        ITaxResolverService taxResolver,
        IStorageService storageService)
    {
        _unitOfWork = unitOfWork;
        _featureGate = featureGate;
        _taxResolver = taxResolver;
        _storageService = storageService;
    }

    #region Public API Methods

    /// <summary>
    /// Retrieves all active products for a branch, grouped by category with sizes and extras.
    /// </summary>
    public async Task<IEnumerable<ProductResponse>> GetAllActiveAsync(int branchId)
    {
        var categories = await _unitOfWork.Categories.GetActiveBranchCategoriesAsync(branchId);
        var ctx = await LoadTaxContextAsync(branchId);

        var products = new List<Product>();
        foreach (var category in categories)
            products.AddRange(await _unitOfWork.Products.GetActiveWithExtrasAsync(category.Id));

        // One batch query for the whole page instead of a per-product COUNT.
        var withOrders = await _unitOfWork.Products.GetProductIdsWithOrdersAsync(
            products.Select(p => p.Id));

        return products.Select(p => MapWithEffectiveTax(p, ctx, withOrders)).ToList();
    }

    /// <summary>
    /// Retrieves a product by its identifier with sizes and extras.
    /// </summary>
    public async Task<ProductResponse> GetByIdAsync(int id)
    {
        var product = await LoadWithRelationsOrThrowAsync(id);
        var ctx = await LoadTaxContextAsync(product.BranchId);
        var withOrders = await LoadOrdersFlagAsync(id);
        return MapWithEffectiveTax(product, ctx, withOrders);
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
        var withOrders = await LoadOrdersFlagAsync(existing.Id);
        return MapWithEffectiveTax(existing, ctx, withOrders);
    }

    /// <summary>
    /// Toggles the active/inactive status of a product.
    /// </summary>
    public async Task<ProductResponse> ToggleActiveAsync(int id)
    {
        // Load with relations so the toggle response carries the product's
        // sizes/modifier groups/images (not just the scalar fields), letting
        // the FE merge the full entity instead of wiping its local copy.
        var product = await _unitOfWork.Products.GetByIdWithRelationsAsync(id);

        if (product == null)
            throw new NotFoundException($"Product with id {id} not found");

        product.IsAvailable = !product.IsAvailable;
        _unitOfWork.Products.Update(product);
        await _unitOfWork.SaveChangesAsync();

        var ctx = await LoadTaxContextAsync(product.BranchId);
        var withOrders = await LoadOrdersFlagAsync(id);
        return MapWithEffectiveTax(product, ctx, withOrders);
    }

    /// <summary>
    /// Hard-deletes a product. See <see cref="IProductService.DeleteAsync"/>.
    /// </summary>
    public async Task<DeleteProductResult> DeleteAsync(int id)
    {
        // Load with relations so EF tracks the cascade children (sizes,
        // modifier groups/extras, images, taxes) and the image URLs are
        // available for the post-commit storage cleanup. The global query
        // filter scopes this to the caller's branch, so a foreign-branch id
        // resolves to NotFound rather than leaking a cross-tenant delete.
        var product = await _unitOfWork.Products.GetByIdWithRelationsAsync(id);
        if (product == null)
            return DeleteProductResult.NotFound();

        // Sales history blocks the delete (OrderItem.Product is OnDelete.Restrict).
        // Pre-check so the FE gets a specific, actionable 409 with the count.
        var orderCount = await _unitOfWork.Products.CountOrderItemsForProductAsync(id);
        if (orderCount > 0)
            return DeleteProductResult.HasOrders(orderCount);

        // Capture blob URLs before the cascade removes the ProductImage rows.
        // Deleted only AFTER the DB delete commits, so an InUse rollback never
        // orphans live blobs.
        var blobUrls = CollectImageBlobUrls(product);

        _unitOfWork.Products.Delete(product);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Safety net for the NoAction foreign keys the pre-check can't see
            // (ProductConsumption / StockReceiptItem). The FK violation rolls
            // the delete back; report a generic in-use conflict.
            return DeleteProductResult.InUse();
        }

        foreach (var url in blobUrls)
            await _storageService.DeleteAsync(url);

        return DeleteProductResult.Deleted();
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

        var withOrders = await LoadOrdersFlagAsync(product.Id);
        return MapWithEffectiveTax(product, ctx, withOrders);
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
        var withOrders = await LoadOrdersFlagAsync(product.Id);
        return MapWithEffectiveTax(product, ctx, withOrders);
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Loads a product with its Sizes/Extras/Images populated or throws
    /// <see cref="NotFoundException"/>. Centralised so all entity-side
    /// reads in the service go through the same include list.
    /// </summary>
    /// <summary>
    /// Gathers the distinct storage URLs to purge when a product is deleted:
    /// every <see cref="ProductImage.Url"/> plus the denormalized
    /// <see cref="Product.ImageUrl"/> when it points at a blob not already
    /// covered by the image rows.
    /// </summary>
    private static List<string> CollectImageBlobUrls(Product product)
    {
        var urls = new HashSet<string>(StringComparer.Ordinal);

        if (product.Images != null)
        {
            foreach (var image in product.Images)
            {
                if (!string.IsNullOrWhiteSpace(image.Url))
                    urls.Add(image.Url);
            }
        }

        if (!string.IsNullOrWhiteSpace(product.ImageUrl))
            urls.Add(product.ImageUrl);

        return urls.ToList();
    }

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

    private ProductResponse MapWithEffectiveTax(Product product, TaxContext ctx, HashSet<int>? withOrders = null)
    {
        var resolution = _taxResolver.ResolveTax(product, ctx.Business, ctx.CountryDefaults);
        var response = product.ToResponse(withOrders);
        response.EffectiveTaxRate = resolution.Rate;
        response.EffectiveIsTaxIncluded = product.IsTaxIncluded;
        return response;
    }

    /// <summary>
    /// Resolves the <c>HasOrders</c> flag for a single product via the batch
    /// repository helper, so the read methods share one code path with the
    /// list endpoint.
    /// </summary>
    private Task<HashSet<int>> LoadOrdersFlagAsync(int productId) =>
        _unitOfWork.Products.GetProductIdsWithOrdersAsync(new[] { productId });

    private sealed record TaxContext(Business Business, IReadOnlyList<Tax> CountryDefaults);

    #endregion
}
