using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IPushSubscriptionRepository : IGenericRepository<PushSubscription>
{
    Task<PushSubscription?> GetByEndpointAsync(string endpoint);
    Task<IEnumerable<PushSubscription>> GetByBranchAsync(int branchId);
    Task<IEnumerable<PushSubscription>> GetByUserAsync(int userId);
}
