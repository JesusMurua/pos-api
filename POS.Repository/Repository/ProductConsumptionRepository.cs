using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class ProductConsumptionRepository : GenericRepository<ProductConsumption>, IProductConsumptionRepository
{
    public ProductConsumptionRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<ProductConsumption>> GetByProductAsync(int productId)
    {
        return await _context.ProductConsumptions
            .Where(pc => pc.ProductId == productId)
            .Include(pc => pc.InventoryItem)
            .ToListAsync();
    }

    public async Task<ProductConsumption?> GetByProductAndItemAsync(int productId, int inventoryItemId)
    {
        return await _context.ProductConsumptions
            .FirstOrDefaultAsync(pc => pc.ProductId == productId && pc.InventoryItemId == inventoryItemId);
    }
}
