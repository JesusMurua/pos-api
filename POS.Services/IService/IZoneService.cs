using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing zones within a branch.
/// </summary>
public interface IZoneService
{
    Task<IEnumerable<Zone>> GetByBranchAsync(int branchId);
    Task<Zone> CreateAsync(int branchId, Zone zone);
    Task<Zone> UpdateAsync(int id, int branchId, Zone zone);
    Task DeleteAsync(int id, int branchId);
}
