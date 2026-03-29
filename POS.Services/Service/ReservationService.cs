using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Domain.Models;
using POS.Repository;
using POS.Services.IService;

namespace POS.Services.Service;

public class ReservationService : IReservationService
{
    private readonly IUnitOfWork _unitOfWork;

    public ReservationService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    #region Public API Methods

    public async Task<IEnumerable<Reservation>> GetByDateAsync(int branchId, DateOnly date)
    {
        return await _unitOfWork.Reservations.GetByBranchAndDateAsync(branchId, date);
    }

    public async Task<IEnumerable<Reservation>> GetByMonthAsync(int branchId, int year, int month)
    {
        var startDate = new DateOnly(year, month, 1);
        var endDate = startDate.AddMonths(1);

        var reservations = await _unitOfWork.Reservations.GetAsync(
            r => r.BranchId == branchId
                && r.ReservationDate >= startDate
                && r.ReservationDate < endDate,
            "Table,CreatedByUser");

        return reservations.OrderBy(r => r.ReservationDate).ThenBy(r => r.ReservationTime);
    }

    public async Task<Reservation> CreateAsync(Reservation reservation, int branchId, int userId)
    {
        reservation.BranchId = branchId;
        reservation.CreatedByUserId = userId;
        reservation.Status = ReservationStatus.Pending;

        if (reservation.TableId.HasValue)
        {
            var available = await _unitOfWork.Reservations.IsTableAvailableAsync(
                reservation.TableId.Value,
                reservation.ReservationDate,
                reservation.ReservationTime,
                reservation.DurationMinutes);

            if (!available)
                throw new ValidationException("Table is not available for the selected date and time.");
        }

        await _unitOfWork.Reservations.AddAsync(reservation);
        await _unitOfWork.SaveChangesAsync();

        return reservation;
    }

    public async Task<Reservation> UpdateAsync(int id, Reservation updated, int branchId)
    {
        var reservation = await GetByIdAndBranchAsync(id, branchId);

        reservation.GuestName = updated.GuestName;
        reservation.GuestPhone = updated.GuestPhone;
        reservation.PartySize = updated.PartySize;
        reservation.ReservationDate = updated.ReservationDate;
        reservation.ReservationTime = updated.ReservationTime;
        reservation.DurationMinutes = updated.DurationMinutes;
        reservation.Status = updated.Status;
        reservation.Notes = updated.Notes;
        reservation.TableId = updated.TableId;

        if (reservation.TableId.HasValue)
        {
            var available = await _unitOfWork.Reservations.IsTableAvailableAsync(
                reservation.TableId.Value,
                reservation.ReservationDate,
                reservation.ReservationTime,
                reservation.DurationMinutes,
                reservation.Id);

            if (!available)
                throw new ValidationException("Table is not available for the selected date and time.");
        }

        _unitOfWork.Reservations.Update(reservation);
        await _unitOfWork.SaveChangesAsync();

        return reservation;
    }

    public async Task CancelAsync(int id, int branchId)
    {
        var reservation = await GetByIdAndBranchAsync(id, branchId);
        reservation.Status = ReservationStatus.Cancelled;
        _unitOfWork.Reservations.Update(reservation);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task MarkNoShowAsync(int id, int branchId)
    {
        var reservation = await GetByIdAndBranchAsync(id, branchId);
        reservation.Status = ReservationStatus.NoShow;
        _unitOfWork.Reservations.Update(reservation);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task SeatAsync(int id, int branchId)
    {
        var reservation = await GetByIdAndBranchAsync(id, branchId);
        reservation.Status = ReservationStatus.Seated;

        if (reservation.TableId.HasValue)
        {
            var table = await _unitOfWork.RestaurantTables.GetByIdAsync(reservation.TableId.Value);
            if (table != null)
            {
                table.Status = "in_kitchen";
                _unitOfWork.RestaurantTables.Update(table);
            }
        }

        _unitOfWork.Reservations.Update(reservation);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<bool> CheckAvailabilityAsync(int tableId, DateOnly date, TimeOnly time, int durationMinutes, int? excludeId = null)
    {
        return await _unitOfWork.Reservations.IsTableAvailableAsync(tableId, date, time, durationMinutes, excludeId);
    }

    #endregion

    #region Private Helper Methods

    private async Task<Reservation> GetByIdAndBranchAsync(int id, int branchId)
    {
        var reservation = await _unitOfWork.Reservations.GetByIdAsync(id);

        if (reservation == null || reservation.BranchId != branchId)
            throw new NotFoundException($"Reservation with id {id} not found.");

        return reservation;
    }

    #endregion
}
