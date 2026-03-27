using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class DeviceActivationCodeRepository : GenericRepository<DeviceActivationCode>, IDeviceActivationCodeRepository
{
    public DeviceActivationCodeRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<DeviceActivationCode?> GetByCodeAsync(string code)
    {
        return await _context.DeviceActivationCodes
            .Include(d => d.Business)
            .Include(d => d.Branch)
            .FirstOrDefaultAsync(d => d.Code == code);
    }

    public async Task<bool> CodeExistsAsync(string code)
    {
        return await _context.DeviceActivationCodes
            .AnyAsync(d => d.Code == code && !d.IsUsed && d.ExpiresAt > DateTime.UtcNow);
    }
}
