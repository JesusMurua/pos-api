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

    /// <summary>
    /// Generates a 6-character alphanumeric link code that lets an
    /// already-activated device redeem its binding to <paramref name="registerId"/>
    /// from the terminal UI. Validates cross-tenant ownership and that the
    /// register is currently unbound; rejects with 400 otherwise. Code lifetime
    /// is 30 minutes.
    /// </summary>
    Task<GenerateLinkCodeResponse> GenerateLinkCodeAsync(int registerId, int branchId);

    /// <summary>
    /// Redeems a previously-generated link code. Called by the device itself
    /// authenticated with its long-lived device JWT (Mode B). The
    /// <paramref name="branchId"/> and <paramref name="deviceId"/> are pulled
    /// from the device JWT — the body never carries device identity.
    /// Uses pessimistic row-level locking on the code row to prevent
    /// double-redemption races.
    /// </summary>
    Task<CashRegisterDto> RedeemLinkCodeAsync(string code, int deviceId, int branchId);

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
