using Microsoft.EntityFrameworkCore;
using POS.Domain.Enums;
using POS.Domain.Models;
using POS.Repository.IRepository;

namespace POS.Repository.Repository;

public class ReservationRepository : GenericRepository<Reservation>, IReservationRepository
{
    public ReservationRepository(ApplicationDbContext context) : base(context)
    {
    }

    public async Task<IEnumerable<Reservation>> GetByBranchAndDateAsync(int branchId, DateOnly date)
    {
        return await _context.Reservations
            .Where(r => r.BranchId == branchId && r.ReservationDate == date)
            .Include(r => r.Table)
            .Include(r => r.CreatedByUser)
            .OrderBy(r => r.ReservationTime)
            .ToListAsync();
    }

    public async Task<bool> IsTableAvailableAsync(int tableId, DateOnly date, TimeOnly time, int durationMinutes, int? excludeId = null)
    {
        var newEnd = time.AddMinutes(durationMinutes);

        var hasOverlap = await _context.Reservations
            .Where(r => r.TableId == tableId
                && r.ReservationDate == date
                && (r.Status == ReservationStatus.Confirmed || r.Status == ReservationStatus.Seated)
                && (excludeId == null || r.Id != excludeId))
            .AnyAsync(r => r.ReservationTime < newEnd
                && r.ReservationTime.AddMinutes(r.DurationMinutes) > time);

        return !hasOverlap;
    }
}
