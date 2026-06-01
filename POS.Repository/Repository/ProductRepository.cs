using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class ProductRepository : GenericRepository<Product>, IProductRepository
{
    public ProductRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Product>> GetActiveWithExtrasAsync(int categoryId)
    {
        return await _context.Products
            .Where(p => p.CategoryId == categoryId && p.IsAvailable)
            .Include(p => p.Sizes)
            .Include(p => p.ModifierGroups!)
                .ThenInclude(g => g.Extras)
            .Include(p => p.Images)
            .Include(p => p.ProductTaxes)
                .ThenInclude(pt => pt.Tax)
            .ToListAsync();
    }

    public async Task<Product?> GetByIdWithRelationsAsync(int id)
    {
        return await _context.Products
            .Include(p => p.Sizes)
            .Include(p => p.ModifierGroups!)
                .ThenInclude(g => g.Extras)
            .Include(p => p.Images)
            .Include(p => p.ProductTaxes)
                .ThenInclude(pt => pt.Tax)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<Product?> GetByBarcodeAsync(int branchId, string barcode)
    {
        return await _context.Products
            .Where(p => p.BranchId == branchId && p.Barcode == barcode && p.IsAvailable)
            .Include(p => p.Category)
            .Include(p => p.Sizes)
            .Include(p => p.ModifierGroups!)
                .ThenInclude(g => g.Extras)
            .Include(p => p.Images)
            .Include(p => p.ProductTaxes)
                .ThenInclude(pt => pt.Tax)
            .FirstOrDefaultAsync();
    }

    public async Task<ProductImage?> GetImageByIdAsync(int imageId)
    {
        return await _context.ProductImages
            .FirstOrDefaultAsync(i => i.Id == imageId);
    }

    public async Task AddImageAsync(ProductImage image)
    {
        await _context.ProductImages.AddAsync(image);
    }

    public void DeleteImage(ProductImage image)
    {
        _context.ProductImages.Remove(image);
    }

    /// <inheritdoc />
    public Task<int> CountForBusinessAsync(int businessId) =>
        _context.Products
            .IgnoreQueryFilters()
            .Where(p => p.Branch!.BusinessId == businessId)
            .CountAsync();
}
