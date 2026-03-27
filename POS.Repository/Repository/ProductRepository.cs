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
            .ToListAsync();
    }

    public async Task<Product?> GetByIdWithRelationsAsync(int id)
    {
        return await _context.Products
            .Include(p => p.Sizes)
            .Include(p => p.Extras)
            .FirstOrDefaultAsync(p => p.Id == id);
    }
}
