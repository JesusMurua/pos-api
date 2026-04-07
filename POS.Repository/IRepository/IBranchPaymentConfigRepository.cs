using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IBranchPaymentConfigRepository : IGenericRepository<BranchPaymentConfig>
{
    Task<IEnumerable<BranchPaymentConfig>> GetByBranchAsync(int branchId);
    Task<BranchPaymentConfig?> GetByBranchAndProviderAsync(int branchId, string provider);
}
