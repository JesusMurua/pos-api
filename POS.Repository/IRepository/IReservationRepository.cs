using POS.Domain.Models;

namespace POS.Repository.IRepository;

public interface IReservationRepository : IGenericRepository<Reservation>
{
    Task<IEnumerable<Reservation>> GetByBranchAndDateAsync(int branchId, DateOnly date);

    Task<bool> IsTableAvailableAsync(int tableId, DateOnly date, TimeOnly time, int durationMinutes, int? excludeId = null);
}
