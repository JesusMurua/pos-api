using POS.Domain.DTOs.CashRegister;
using POS.Domain.Models;

namespace POS.Services.IService;

/// <summary>
/// Provides operations for managing cash registers, sessions, and movements.
/// </summary>
public interface ICashRegisterService
{
    #region Cash Register CRUD

    /// <summary>
    /// Gets all cash registers for a branch, projected to <see cref="CashRegisterDto"/>.
    /// </summary>
    Task<IEnumerable<CashRegisterDto>> GetAllRegistersAsync(int branchId);

    /// <summary>
    /// Creates a new cash register for a branch.
    /// </summary>
    Task<CashRegisterDto> CreateRegisterAsync(int branchId, CreateCashRegisterRequest request);

    /// <summary>
    /// Updates a cash register's name and/or bound device.
    /// </summary>
    Task<CashRegisterDto> UpdateRegisterAsync(int registerId, int branchId, UpdateCashRegisterRequest request);

    /// <summary>
    /// Toggles a cash register's active status.
    /// </summary>
    Task<CashRegisterDto> ToggleRegisterAsync(int registerId, int branchId);

    /// <summary>
    /// Links a physical device to a cash register by UUID.
    /// </summary>
    Task<CashRegisterDto> LinkDeviceAsync(int registerId, int branchId, LinkDeviceRequest request);

    /// <summary>
    /// Gets a cash register by its bound device UUID. Single-query JOIN.
    /// </summary>
    Task<CashRegisterDto?> GetRegisterByDeviceUuidAsync(int branchId, string deviceUuid);

    #endregion

    #region Session Operations

    /// <summary>
    /// Gets the current open session for a specific register, or for the branch (legacy).
    /// </summary>
    Task<CashRegisterSessionDto?> GetOpenSessionAsync(int branchId, int? cashRegisterId = null);

    /// <summary>
    /// Opens a new cash register session, optionally tied to a specific register.
    /// The opener identity is taken from the JWT (<paramref name="userId"/>),
    /// not from the request body.
    /// </summary>
    Task<CashRegisterSessionDto> OpenSessionAsync(int branchId, int userId, OpenSessionRequest request);

    /// <summary>
    /// Closes the current open session. The closer identity is taken from the JWT.
    /// </summary>
    Task<CashRegisterSessionDto> CloseSessionAsync(int branchId, int userId, CloseSessionRequest request, int? cashRegisterId = null);

    /// <summary>
    /// Adds a cash movement to the open session. The author identity is taken from the JWT.
    /// </summary>
    Task<CashMovementDto> AddMovementAsync(int branchId, int userId, AddMovementRequest request, int? cashRegisterId = null);

    /// <summary>
    /// Gets cash register session history for an inclusive local calendar date
    /// range <c>[from, to]</c>.
    /// </summary>
    Task<IEnumerable<CashRegisterSessionDto>> GetHistoryAsync(int branchId, DateOnly from, DateOnly to);

    #endregion
}
