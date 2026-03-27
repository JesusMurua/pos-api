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
            .Include(p => p.Extras)
            .Include(p => p.Images)
            .ToListAsync();
    }

    public async Task<Product?> GetByIdWithRelationsAsync(int id)
    {
        return await _context.Products
            .Include(p => p.Sizes)
            .Include(p => p.Extras)
            .Include(p => p.Images)
            .FirstOrDefaultAsync(p => p.Id == id);
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
}
