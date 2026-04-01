using POS.Domain.Enums;
using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IBranchDeliveryConfigRepository : IGenericRepository<BranchDeliveryConfig>
{
    /// <summary>Gets all delivery configs for a branch.</summary>
    Task<IEnumerable<BranchDeliveryConfig>> GetByBranchAsync(int branchId);

    /// <summary>Gets a specific platform config for a branch.</summary>
    Task<BranchDeliveryConfig?> GetByBranchAndPlatformAsync(int branchId, OrderSource platform);

    /// <summary>Returns true if branch has at least one active config.</summary>
    Task<bool> HasActiveConfigAsync(int branchId);
}
