using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class CategoryRepository : GenericRepository<Category>, ICategoryRepository
{
    public CategoryRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Category>> GetActiveBranchCategoriesAsync(int branchId)
    {
        return await _context.Categories
            .Where(c => c.BranchId == branchId && c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();
    }
}
