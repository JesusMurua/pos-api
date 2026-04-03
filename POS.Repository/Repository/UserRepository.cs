using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class UserRepository : GenericRepository<User>, IUserRepository
{
    public UserRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<IEnumerable<User>> GetActiveByBranchAsync(int branchId)
    {
        return await _context.Users
            .Where(u => u.IsActive
                && u.PinHash != null
                && u.UserBranches!.Any(ub => ub.BranchId == branchId))
            .ToListAsync();
    }
}
