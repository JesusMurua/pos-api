using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class InventoryRepository : GenericRepository<InventoryItem>, IInventoryRepository
{
    public InventoryRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<InventoryItem>> GetAllByBranchAsync(int branchId)
    {
        return await _context.InventoryItems
            .Where(i => i.BranchId == branchId && i.IsActive)
            .OrderBy(i => i.Name)
            .ToListAsync();
    }

    public async Task<IEnumerable<InventoryItem>> GetLowStockAsync(int branchId)
    {
        return await _context.InventoryItems
            .Where(i => i.BranchId == branchId && i.IsActive && i.CurrentStock <= i.LowStockThreshold)
            .OrderBy(i => i.CurrentStock)
            .ToListAsync();
    }

    public async Task<InventoryItem?> GetWithMovementsAsync(int id)
    {
        return await _context.InventoryItems
            .Include(i => i.Movements!.OrderByDescending(m => m.CreatedAt))
            .FirstOrDefaultAsync(i => i.Id == id);
    }
}
