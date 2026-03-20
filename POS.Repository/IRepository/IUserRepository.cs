using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IUserRepository : IGenericRepository<User>
{
    Task<User?> GetByEmailAsync(string email);

    Task<IEnumerable<User>> GetActiveByBranchAsync(int branchId);
}
