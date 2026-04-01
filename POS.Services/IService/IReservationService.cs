using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing reservations.
/// </summary>
public interface IReservationService
{
    Task<IEnumerable<Reservation>> GetByDateAsync(int branchId, DateOnly date);

    Task<IEnumerable<Reservation>> GetByMonthAsync(int branchId, int year, int month);

    Task<Reservation> CreateAsync(Reservation reservation, int branchId, int userId);

    Task<Reservation> UpdateAsync(int id, Reservation reservation, int branchId);

    Task ConfirmAsync(int id, int branchId);

    Task CancelAsync(int id, int branchId);

    Task MarkNoShowAsync(int id, int branchId);

    Task SeatAsync(int id, int branchId);

    Task<bool> CheckAvailabilityAsync(int tableId, DateOnly date, TimeOnly time, int durationMinutes, int? excludeId = null);
}
