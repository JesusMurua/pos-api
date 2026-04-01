using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class BranchDeliveryConfigRepository
    : GenericRepository<BranchDeliveryConfig>, IBranchDeliveryConfigRepository
{
    public BranchDeliveryConfigRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<BranchDeliveryConfig>> GetByBranchAsync(int branchId)
    {
        return await _context.BranchDeliveryConfigs
            .Where(c => c.BranchId == branchId)
            .OrderBy(c => c.Platform)
            .ToListAsync();
    }

    public async Task<BranchDeliveryConfig?> GetByBranchAndPlatformAsync(
        int branchId, OrderSource platform)
    {
        return await _context.BranchDeliveryConfigs
            .FirstOrDefaultAsync(c => c.BranchId == branchId && c.Platform == platform);
    }

    public async Task<bool> HasActiveConfigAsync(int branchId)
    {
        return await _context.BranchDeliveryConfigs
            .AnyAsync(c => c.BranchId == branchId && c.IsActive);
    }
}
