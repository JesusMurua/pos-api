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
            .Where(r => r.BranchId == branchId)
            .OrderBy(r => r.Name)
            .ToListAsync();
    }

    public async Task<CashRegister?> GetByDeviceUuidAsync(int branchId, string deviceUuid)
    {
        return await _context.CashRegisters
            .FirstOrDefaultAsync(r => r.BranchId == branchId && r.DeviceUuid == deviceUuid);
    }

    public async Task<CashRegister?> GetByNameAsync(int branchId, string name)
    {
        return await _context.CashRegisters
            .FirstOrDefaultAsync(r => r.BranchId == branchId && r.Name == name);
    }
}
