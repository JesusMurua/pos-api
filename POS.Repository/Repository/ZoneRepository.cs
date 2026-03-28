using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class ZoneRepository : GenericRepository<Zone>, IZoneRepository
{
    public ZoneRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Zone>> GetByBranchAsync(int branchId)
    {
        return await _context.Zones
            .Where(z => z.BranchId == branchId)
            .OrderBy(z => z.SortOrder)
            .ToListAsync();
    }
}
