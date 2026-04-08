using Microsoft.EntityFrameworkCore;
using POS.Domain.Helpers;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class CashRegisterSessionRepository : GenericRepository<CashRegisterSession>, ICashRegisterSessionRepository
{
    public CashRegisterSessionRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<CashRegisterSession?> GetOpenSessionAsync(int branchId)
    {
        return await _context.CashRegisterSessions
            .Where(s => s.BranchId == branchId && s.CashRegisterStatusId == CashRegisterStatus.Open)
            .Include(s => s.Movements)
            .FirstOrDefaultAsync();
    }

    public async Task<CashRegisterSession?> GetOpenSessionByRegisterAsync(int registerId)
    {
        return await _context.CashRegisterSessions
            .Where(s => s.CashRegisterId == registerId && s.CashRegisterStatusId == CashRegisterStatus.Open)
            .Include(s => s.Movements)
            .FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<CashRegisterSession>> GetHistoryAsync(int branchId, DateTime from, DateTime to)
    {
        return await _context.CashRegisterSessions
            .Where(s => s.BranchId == branchId
                && s.OpenedAt >= from
                && s.OpenedAt <= to)
            .Include(s => s.Movements)
            .OrderByDescending(s => s.OpenedAt)
            .ToListAsync();
    }
}
