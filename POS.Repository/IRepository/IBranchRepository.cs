using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IBranchRepository : IGenericRepository<Branch>
{
    Task<Branch?> GetByIdWithConfigAsync(int branchId);
}
