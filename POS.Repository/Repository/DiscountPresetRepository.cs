using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class DiscountPresetRepository : GenericRepository<DiscountPreset>, IDiscountPresetRepository
{
    public DiscountPresetRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<DiscountPreset>> GetByBranchAsync(int branchId)
    {
        return await _context.DiscountPresets
            .Where(d => d.BranchId == branchId && d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync();
    }
}
