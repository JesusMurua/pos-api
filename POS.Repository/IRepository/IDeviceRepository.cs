using POS.Domain.DTOs.Device;
using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IDeviceRepository : IGenericRepository<Device>
{
    /// <summary>
    /// Lightweight existence + active check used by the per-request device
    /// authorization filter. Returns <c>null</c> when the device does not exist,
    /// otherwise the current <c>IsActive</c> flag. Must use <c>AsNoTracking</c>
    /// and a single-column projection on the PK index.
    /// </summary>
    Task<bool?> GetIsActiveAsync(int deviceId);

    Task<Device?> GetByDeviceUuidAsync(string deviceUuid);

    /// <summary>
    /// Tenant-scoped fetch used by admin mutations. Returns the tracked entity
    /// when both the id and the caller's business match, otherwise <c>null</c>.
    /// The tenant filter is applied at the SQL level via <c>Branch.BusinessId</c>
    /// so cross-tenant targets never reach application code.
    /// </summary>
    Task<Device?> GetForTenantAsync(int deviceId, int businessId);

    /// <summary>
    /// Projects the full list of devices owned by <paramref name="businessId"/>
    /// (optionally narrowed by <paramref name="branchId"/>) into
    /// <see cref="DeviceListItemResponse"/> — includes <c>Branch.Name</c> via a
    /// single SQL join, ordered by branch then name.
    /// </summary>
    Task<IReadOnlyList<DeviceListItemResponse>> ListProjectedAsync(int businessId, int? branchId);

    /// <summary>
    /// Projects a single device into <see cref="DeviceListItemResponse"/> by id.
    /// Used by <c>PATCH /api/devices/{id}</c> to return the post-update shape
    /// with a fresh <c>BranchName</c>. Returns <c>null</c> when the device does
    /// not exist.
    /// </summary>
    Task<DeviceListItemResponse?> GetProjectedByIdAsync(int deviceId);
}
