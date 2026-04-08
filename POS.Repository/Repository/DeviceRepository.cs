using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class DeviceRepository : GenericRepository<Device>, IDeviceRepository
{
    public DeviceRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Device>> GetByBranchAsync(int branchId)
    {
        return await _context.Devices
            .Where(d => d.BranchId == branchId)
            .OrderBy(d => d.Name)
            .ToListAsync();
    }

    public async Task<Device?> GetByDeviceUuidAsync(string deviceUuid)
    {
        return await _context.Devices
            .FirstOrDefaultAsync(d => d.DeviceUuid == deviceUuid);
    }
}
