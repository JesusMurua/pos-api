using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class BranchPaymentConfigRepository : GenericRepository<BranchPaymentConfig>, IBranchPaymentConfigRepository
{
    public BranchPaymentConfigRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<BranchPaymentConfig>> GetByBranchAsync(int branchId)
    {
        return await _context.BranchPaymentConfigs
            .Where(c => c.BranchId == branchId)
            .OrderBy(c => c.Provider)
            .ToListAsync();
    }

    public async Task<BranchPaymentConfig?> GetByBranchAndProviderAsync(int branchId, string provider)
    {
        return await _context.BranchPaymentConfigs
            .FirstOrDefaultAsync(c => c.BranchId == branchId && c.Provider == provider);
    }
}
