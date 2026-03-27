using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

/// <summary>
/// Repository implementation for user-branch assignments.
/// </summary>
public class UserBranchRepository : GenericRepository<UserBranch>, IUserBranchRepository
{
    public UserBranchRepository(ApplicationDbContext context) : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<IEnumerable<UserBranch>> GetByUserIdAsync(int userId)
    {
        return await _context.UserBranches
            .Include(ub => ub.Branch)
            .Where(ub => ub.UserId == userId)
            .OrderBy(ub => ub.BranchId)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<IEnumerable<UserBranch>> GetByBranchIdAsync(int branchId)
    {
        return await _context.UserBranches
            .Include(ub => ub.User)
            .Where(ub => ub.BranchId == branchId)
            .ToListAsync();
    }
}
