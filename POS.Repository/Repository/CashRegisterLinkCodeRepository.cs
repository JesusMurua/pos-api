using Microsoft.EntityFrameworkCore;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class CashRegisterLinkCodeRepository : GenericRepository<CashRegisterLinkCode>, ICashRegisterLinkCodeRepository
{
    public CashRegisterLinkCodeRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<bool> CodeExistsAsync(string code)
    {
        return await _context.CashRegisterLinkCodes.AnyAsync(c => c.Code == code);
    }

    public async Task<CashRegisterLinkCode?> GetByCodeForUpdateAsync(string code)
    {
        // Raw SQL with FOR UPDATE — composing this with .Include() would let EF
        // wrap the query in a subselect and silently break the row lock, so we
        // load the CashRegister navigation explicitly afterwards (same DbContext,
        // same tx).
        var linkCode = await _context.CashRegisterLinkCodes
            .FromSqlInterpolated($"SELECT * FROM \"CashRegisterLinkCodes\" WHERE \"Code\" = {code} FOR UPDATE")
            .AsTracking()
            .FirstOrDefaultAsync();

        if (linkCode == null) return null;

        await _context.Entry(linkCode).Reference(c => c.CashRegister).LoadAsync();

        return linkCode;
    }
}
