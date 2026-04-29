using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class CashRegisterRepository : GenericRepository<CashRegister>, ICashRegisterRepository
{
    public CashRegisterRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<CashRegister>> GetByBranchAsync(int branchId)
    {
        return await _context.CashRegisters
            .Include(r => r.Device)
            .Where(r => r.BranchId == branchId)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<CashRegister?> GetByDeviceUuidAsync(int branchId, string deviceUuid)
    {
        return await _context.CashRegisters
            .Include(r => r.Device)
            .FirstOrDefaultAsync(r => r.BranchId == branchId
                && r.Device != null
                && r.Device.DeviceUuid == deviceUuid);
    }

    public async Task<CashRegister?> GetByDeviceIdAsync(int branchId, int deviceId)
    {
        return await _context.CashRegisters
            .FirstOrDefaultAsync(r => r.BranchId == branchId && r.DeviceId == deviceId);
    }

    public async Task<CashRegister?> GetByIdWithDeviceAsync(int id)
    {
        return await _context.CashRegisters
            .Include(r => r.Device)
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<CashRegister?> GetByNameAsync(int branchId, string name)
    {
        var normalizedName = name.Trim().ToLowerInvariant();
        return await _context.CashRegisters
            .Include(r => r.Device)
            .FirstOrDefaultAsync(r => r.BranchId == branchId && r.Name == normalizedName);
    }
}
