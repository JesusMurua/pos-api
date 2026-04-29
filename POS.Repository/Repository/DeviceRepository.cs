using Microsoft.EntityFrameworkCore;
using POS.Domain.DTOs.Device;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class DeviceRepository : GenericRepository<Device>, IDeviceRepository
{
    public DeviceRepository(ApplicationDbContext context) : base(context)
    {
    }

    /// <inheritdoc />
    public async Task<bool?> GetIsActiveAsync(int deviceId)
    {
        return await _context.Devices
            .AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => (bool?)d.IsActive)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<Device?> GetByDeviceUuidAsync(string deviceUuid)
    {
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.DeviceUuid == deviceUuid);
    }

    /// <inheritdoc />
    public async Task<Device?> GetByDeviceUuidAndBranchAsync(string deviceUuid, int branchId)
    {
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.DeviceUuid == deviceUuid && d.BranchId == branchId);
    }

    /// <inheritdoc />
    public async Task<Device?> GetForTenantAsync(int deviceId, int businessId)
    {
        // Tenancy projected through Branch because Device has no direct BusinessId
        // FK — a pre-BDD-014 schema decision. The join is index-backed via
        // IX_Devices_BranchId and the PK of Branches.
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.Id == deviceId && d.Branch!.BusinessId == businessId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceListItemResponse>> ListProjectedAsync(int businessId, int? branchId)
    {
        var query = _context.Devices
            .AsNoTracking()
            .Where(d => d.Branch!.BusinessId == businessId);

        if (branchId.HasValue)
            query = query.Where(d => d.BranchId == branchId.Value);

        return await query
            .OrderBy(d => d.BranchId)
            .ThenBy(d => d.Name)
            .Select(d => new DeviceListItemResponse
            {
                Id = d.Id,
                DeviceUuid = d.DeviceUuid,
                Name = d.Name,
                Mode = d.Mode,
                IsActive = d.IsActive,
                BranchId = d.BranchId,
                BranchName = d.Branch!.Name,
                LastSeenAt = d.LastSeenAt,
                CreatedAt = d.CreatedAt
            })
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<DeviceListItemResponse?> GetProjectedByIdAsync(int deviceId)
    {
        return await _context.Devices
            .AsNoTracking()
            .Where(d => d.Id == deviceId)
            .Select(d => new DeviceListItemResponse
            {
                Id = d.Id,
                DeviceUuid = d.DeviceUuid,
                Name = d.Name,
                Mode = d.Mode,
                IsActive = d.IsActive,
                BranchId = d.BranchId,
                BranchName = d.Branch!.Name,
                LastSeenAt = d.LastSeenAt,
                CreatedAt = d.CreatedAt
            })
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<int> CountActiveByModeAsync(int businessId, int? branchId, string mode)
    {
        var query = _context.Devices
            .AsNoTracking()
            .Where(d => d.IsActive && d.Mode == mode && d.Branch!.BusinessId == businessId);

        if (branchId.HasValue)
            query = query.Where(d => d.BranchId == branchId.Value);

        return await query.CountAsync();
    }
}
