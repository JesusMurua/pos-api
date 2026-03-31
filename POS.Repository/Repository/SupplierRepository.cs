using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class SupplierRepository : GenericRepository<Supplier>, ISupplierRepository
{
    public SupplierRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Supplier>> GetAllByBranchAsync(int branchId)
    {
        return await _context.Suppliers
            .Where(s => s.BranchId == branchId && s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<Supplier?> GetWithReceiptsAsync(int id)
    {
        return await _context.Suppliers
            .Include(s => s.StockReceipts!.OrderByDescending(r => r.ReceivedAt))
            .FirstOrDefaultAsync(s => s.Id == id);
    }
}
