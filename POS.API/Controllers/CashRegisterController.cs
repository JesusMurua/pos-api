using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing cash registers, sessions, and movements.
/// </summary>
[Route("api/[controller]")]
public class CashRegisterController : BaseApiController
{
    private readonly ICashRegisterService _cashRegisterService;

    public CashRegisterController(ICashRegisterService cashRegisterService)
    {
        _cashRegisterService = cashRegisterService;
    }

    #region Cash Register CRUD

    /// <summary>
    /// Gets all cash registers for the current branch.
    /// </summary>
    [HttpGet("registers")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<CashRegister>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRegisters()
    {
        var registers = await _cashRegisterService.GetAllRegistersAsync(BranchId);
        return Ok(registers);
    }

    /// <summary>
    /// Creates a new cash register for the current branch.
    /// </summary>
    [HttpPost("registers")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(CashRegister), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateRegister([FromBody] CreateCashRegisterRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var register = await _cashRegisterService.CreateRegisterAsync(BranchId, request);
        return Ok(register);
    }

    /// <summary>
    /// Updates a cash register's name and/or device UUID.
    /// </summary>
    [HttpPut("registers/{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(CashRegister), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateRegister(int id, [FromBody] UpdateCashRegisterRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var register = await _cashRegisterService.UpdateRegisterAsync(id, BranchId, request);
        return Ok(register);
    }

    /// <summary>
    /// Toggles a cash register's active status.
    /// </summary>
    [HttpPatch("registers/{id}/toggle")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(CashRegister), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ToggleRegister(int id)
    {
        var register = await _cashRegisterService.ToggleRegisterAsync(id, BranchId);
        return Ok(register);
    }

    /// <summary>
    /// Gets a cash register by its bound device UUID.
    /// </summary>
    [HttpGet("registers/by-device/{deviceUuid}")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(CashRegister), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetRegisterByDeviceUuid(string deviceUuid)
    {
        var register = await _cashRegisterService.GetRegisterByDeviceUuidAsync(BranchId, deviceUuid);

        if (register == null)
            return NoContent();

        return Ok(register);
    }

    #endregion

    #region Session Operations

    /// <summary>
    /// Gets the current open session. If registerId is provided, fetches by register;
    /// otherwise fetches by branch (legacy single-till behavior).
    /// </summary>
    [HttpGet("session")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(CashRegisterSession), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetOpenSession([FromQuery] int? registerId = null)
    {
        var session = await _cashRegisterService.GetOpenSessionAsync(BranchId, registerId);

        if (session == null)
            return NoContent();

        return Ok(session);
    }

    /// <summary>
    /// Opens a new cash register session. If CashRegisterId is set in the body,
    /// the session is tied to that register (multi-till). Otherwise, legacy behavior.
    /// </summary>
    [HttpPost("session/open")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(CashRegisterSession), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> OpenSession([FromBody] OpenSessionRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var session = await _cashRegisterService.OpenSessionAsync(BranchId, request);
        return Ok(session);
    }

    /// <summary>
    /// Closes the current open session. If registerId is provided, closes by register;
    /// otherwise closes by branch (legacy).
    /// </summary>
    [HttpPost("session/close")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(CashRegisterSession), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CloseSession(
        [FromBody] CloseSessionRequest request,
        [FromQuery] int? registerId = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var session = await _cashRegisterService.CloseSessionAsync(BranchId, request, registerId);
        return Ok(session);
    }

    /// <summary>
    /// Adds a movement to the open session. If registerId is provided, targets that register;
    /// otherwise targets the branch session (legacy).
    /// </summary>
    [HttpPost("movement")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(CashMovement), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddMovement(
        [FromBody] AddMovementRequest request,
        [FromQuery] int? registerId = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var movement = await _cashRegisterService.AddMovementAsync(BranchId, request, registerId);
        return Ok(movement);
    }

    /// <summary>
    /// Gets session history for a date range (all registers for the branch).
    /// </summary>
    [HttpGet("history")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(IEnumerable<CashRegisterSession>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var sessions = await _cashRegisterService.GetHistoryAsync(BranchId, from, to);
        return Ok(sessions);
    }

    #endregion
}
