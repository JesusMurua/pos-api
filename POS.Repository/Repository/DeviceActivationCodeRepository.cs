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

    public async Task<DeviceActivationCode?> GetByCodeForUpdateAsync(string code)
    {
        // Raw SQL with FOR UPDATE — composing this with .Include() would let EF
        // wrap the query in a subselect and silently break the row lock, so we
        // load navigations explicitly afterwards (same DbContext, same tx).
        var activation = await _context.DeviceActivationCodes
            .FromSqlInterpolated($"SELECT * FROM \"DeviceActivationCodes\" WHERE \"Code\" = {code} FOR UPDATE")
            .AsTracking()
            .FirstOrDefaultAsync();

        if (activation == null) return null;

        await _context.Entry(activation).Reference(a => a.Business).LoadAsync();
        await _context.Entry(activation).Reference(a => a.Branch).LoadAsync();

        return activation;
    }
}
