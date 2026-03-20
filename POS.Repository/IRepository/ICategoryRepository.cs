using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface ICategoryRepository : IGenericRepository<Category>
{
    Task<IEnumerable<Category>> GetActiveBranchCategoriesAsync(int branchId);
}
