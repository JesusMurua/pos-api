using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class StockReceiptRepository : GenericRepository<StockReceipt>, IStockReceiptRepository
{
    public StockReceiptRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<StockReceipt>> GetAllByBranchAsync(
        int branchId,
        int? supplierId = null,
        DateTime? from = null,
        DateTime? to = null)
    {
        var query = _context.StockReceipts
            .Where(r => r.BranchId == branchId);

        if (supplierId.HasValue)
            query = query.Where(r => r.SupplierId == supplierId.Value);

        if (from.HasValue)
            query = query.Where(r => r.ReceivedAt >= from.Value);

        if (to.HasValue)
            query = query.Where(r => r.ReceivedAt <= to.Value);

        return await query
            .Include(r => r.Supplier)
            .Include(r => r.ReceivedBy)
            .OrderByDescending(r => r.ReceivedAt)
            .ToListAsync();
    }

    public async Task<StockReceipt?> GetWithItemsAsync(int id)
    {
        return await _context.StockReceipts
            .Include(r => r.Items)
                .ThenInclude(i => i.InventoryItem)
            .Include(r => r.Items)
                .ThenInclude(i => i.Product)
            .Include(r => r.Supplier)
            .Include(r => r.ReceivedBy)
            .FirstOrDefaultAsync(r => r.Id == id);
    }
}
