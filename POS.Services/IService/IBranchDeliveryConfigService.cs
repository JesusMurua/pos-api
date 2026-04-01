using POS.Domain.Enums;
using POS.Domain.Models;

namespace POS.Services.IService;

public interface IBranchDeliveryConfigService
{
    /// <summary>Gets all delivery platform configs for a branch as DTOs.</summary>
    Task<IEnumerable<BranchDeliveryConfigDto>> GetByBranchAsync(int branchId, string baseUrl);

    /// <summary>Creates or updates a platform config. Updates Branch.HasDelivery.</summary>
    Task<BranchDeliveryConfigDto> UpsertAsync(int branchId, UpsertDeliveryConfigRequest request, string baseUrl);

    /// <summary>Deletes a platform config. Updates Branch.HasDelivery.</summary>
    Task DeleteAsync(int branchId, OrderSource platform);
}
