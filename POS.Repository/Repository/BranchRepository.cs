using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class BranchRepository : GenericRepository<Branch>, IBranchRepository
{
    public BranchRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<Branch?> GetByIdWithConfigAsync(int branchId)
    {
        return await _context.Branches
            .Include(b => b.Business)
            .FirstOrDefaultAsync(b => b.Id == branchId);
    }
}
