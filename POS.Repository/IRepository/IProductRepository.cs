using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IProductRepository : IGenericRepository<Product>
{
    Task<IEnumerable<Product>> GetActiveWithExtrasAsync(int categoryId);
    Task<Product?> GetByIdWithRelationsAsync(int id);
    Task<Product?> GetByBarcodeAsync(int branchId, string barcode);
    Task<ProductImage?> GetImageByIdAsync(int imageId);
    Task AddImageAsync(ProductImage image);
    void DeleteImage(ProductImage image);

    /// <summary>
    /// Counts every product across every branch of <paramref name="businessId"/>,
    /// bypassing the BDD-019 branch query filter via <c>IgnoreQueryFilters</c>.
    /// Used by <c>AuthResponse.Snapshot</c> to surface business-wide totals
    /// without per-metric round-trips from the welcome screen.
    /// </summary>
    Task<int> CountForBusinessAsync(int businessId);

    /// <summary>
    /// Counts the <c>OrderItem</c> rows referencing <paramref name="productId"/>.
    /// Drives the hard-delete guard: <c>OrderItem.Product</c> is mapped
    /// <c>OnDelete(Restrict)</c>, so a product that has ever been sold cannot be
    /// removed. A non-zero count means the caller must deactivate (toggle) the
    /// product instead of deleting it.
    /// </summary>
    Task<int> CountOrderItemsForProductAsync(int productId);
}
