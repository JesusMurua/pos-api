using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing cash registers, sessions, and movements.
/// </summary>
public interface ICashRegisterService
{
    #region Cash Register CRUD

    /// <summary>
    /// Gets all cash registers for a branch.
    /// </summary>
    Task<IEnumerable<CashRegister>> GetAllRegistersAsync(int branchId);

    /// <summary>
    /// Creates a new cash register for a branch.
    /// </summary>
    Task<CashRegister> CreateRegisterAsync(int branchId, CreateCashRegisterRequest request);

    /// <summary>
    /// Updates a cash register's name and/or device UUID.
    /// </summary>
    Task<CashRegister> UpdateRegisterAsync(int registerId, int branchId, UpdateCashRegisterRequest request);

    /// <summary>
    /// Toggles a cash register's active status.
    /// </summary>
    Task<CashRegister> ToggleRegisterAsync(int registerId, int branchId);

    /// <summary>
    /// Gets a cash register by its bound device UUID.
    /// </summary>
    Task<CashRegister?> GetRegisterByDeviceUuidAsync(int branchId, string deviceUuid);

    #endregion

    #region Session Operations

    /// <summary>
    /// Gets the current open session for a specific register, or for the branch (legacy).
    /// </summary>
    Task<CashRegisterSession?> GetOpenSessionAsync(int branchId, int? cashRegisterId = null);

    /// <summary>
    /// Opens a new cash register session, optionally tied to a specific register.
    /// </summary>
    Task<CashRegisterSession> OpenSessionAsync(int branchId, OpenSessionRequest request);

    /// <summary>
    /// Closes the current open session for a specific register, or for the branch (legacy).
    /// </summary>
    Task<CashRegisterSession> CloseSessionAsync(int branchId, CloseSessionRequest request, int? cashRegisterId = null);

    /// <summary>
    /// Adds a cash movement to the open session of a specific register, or for the branch (legacy).
    /// </summary>
    Task<CashMovement> AddMovementAsync(int branchId, AddMovementRequest request, int? cashRegisterId = null);

    /// <summary>
    /// Gets cash register session history for a date range.
    /// </summary>
    Task<IEnumerable<CashRegisterSession>> GetHistoryAsync(int branchId, DateTime from, DateTime to);

    #endregion
}
