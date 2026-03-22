using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing cash register sessions and movements.
/// </summary>
public interface ICashRegisterService
{
    /// <summary>
    /// Gets the current open session for a branch.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <returns>The open session or null if none exists.</returns>
    Task<CashRegisterSession?> GetOpenSessionAsync(int branchId);

    /// <summary>
    /// Opens a new cash register session.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="request">The session opening data.</param>
    /// <returns>The created session.</returns>
    /// <exception cref="POS.Domain.Exceptions.ValidationException">Thrown when there is already an open session.</exception>
    Task<CashRegisterSession> OpenSessionAsync(int branchId, OpenSessionRequest request);

    /// <summary>
    /// Closes the current open session.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="request">The session closing data.</param>
    /// <returns>The closed session.</returns>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when no open session exists.</exception>
    Task<CashRegisterSession> CloseSessionAsync(int branchId, CloseSessionRequest request);

    /// <summary>
    /// Adds a cash movement to the current open session.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="request">The movement data.</param>
    /// <returns>The created movement.</returns>
    /// <exception cref="POS.Domain.Exceptions.NotFoundException">Thrown when no open session exists.</exception>
    Task<CashMovement> AddMovementAsync(int branchId, AddMovementRequest request);

    /// <summary>
    /// Gets cash register history for a date range.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>Sessions within the date range.</returns>
    Task<IEnumerable<CashRegisterSession>> GetHistoryAsync(int branchId, DateTime from, DateTime to);
}
