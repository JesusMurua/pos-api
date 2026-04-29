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
            .Include(s => s.OpenedByUser)
            .Include(s => s.ClosedByUser)
            .FirstOrDefaultAsync();
    }

    public async Task<CashRegisterSession?> GetOpenSessionByRegisterAsync(int registerId)
    {
        return await _context.CashRegisterSessions
            .Where(s => s.CashRegisterId == registerId && s.CashRegisterStatusId == CashRegisterStatus.Open)
            .Include(s => s.Movements)
            .Include(s => s.OpenedByUser)
            .Include(s => s.ClosedByUser)
            .FirstOrDefaultAsync();
    }

    public async Task<CashRegisterSession?> GetByIdWithUsersAsync(int id)
    {
        return await _context.CashRegisterSessions
            .Include(s => s.OpenedByUser)
            .Include(s => s.ClosedByUser)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<IEnumerable<CashRegisterSession>> GetHistoryAsync(int branchId, DateTime startUtc, DateTime endUtc)
    {
        EnsureUtcRange(startUtc, endUtc);

        return await _context.CashRegisterSessions
            .Where(s => s.BranchId == branchId
                && s.OpenedAt >= startUtc
                && s.OpenedAt < endUtc)
            .Include(s => s.Movements)
            .Include(s => s.OpenedByUser)
            .Include(s => s.ClosedByUser)
            .OrderByDescending(s => s.OpenedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Defensive guard (BDD-013 VR-002/VR-003). Rejects non-UTC bounds or
    /// collapsed ranges so a future caller that bypasses the service layer
    /// cannot re-introduce <c>Npgsql Kind=Unspecified</c> errors nor silently
    /// return zero rows from a range where <c>startUtc &gt;= endUtc</c>.
    /// </summary>
    private static void EnsureUtcRange(DateTime startUtc, DateTime endUtc)
    {
        if (startUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("startUtc must have DateTimeKind.Utc", nameof(startUtc));
        if (endUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("endUtc must have DateTimeKind.Utc", nameof(endUtc));
        if (endUtc <= startUtc)
            throw new ArgumentException("endUtc must be strictly greater than startUtc", nameof(endUtc));
    }
}
