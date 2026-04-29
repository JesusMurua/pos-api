using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.DTOs.CashRegister;
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
    [ProducesResponseType(typeof(IEnumerable<CashRegisterDto>), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(CashRegisterDto), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(CashRegisterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> UpdateRegister(int id, [FromBody] UpdateCashRegisterRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var register = await _cashRegisterService.UpdateRegisterAsync(id, BranchId, request);
        return Ok(register);
    }

    /// <summary>
    /// Links a physical device to a cash register by UUID.
    /// </summary>
    /// <param name="id">Cash register identifier.</param>
    /// <param name="request">Device UUID to link.</param>
    /// <returns>The updated cash register.</returns>
    /// <response code="200">Device linked successfully.</response>
    /// <response code="400">If the UUID is invalid or already in use.</response>
    /// <response code="404">If the cash register is not found.</response>
    [HttpPatch("registers/{id}/link-device")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(CashRegisterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> LinkDevice(int id, [FromBody] LinkDeviceRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var register = await _cashRegisterService.LinkDeviceAsync(id, BranchId, request);
        return Ok(register);
    }

    /// <summary>
    /// Toggles a cash register's active status.
    /// </summary>
    [HttpPatch("registers/{id}/toggle")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(CashRegisterDto), StatusCodes.Status200OK)]
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
    [ProducesResponseType(typeof(CashRegisterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetRegisterByDeviceUuid(string deviceUuid)
    {
        var register = await _cashRegisterService.GetRegisterByDeviceUuidAsync(BranchId, deviceUuid);

        if (register == null)
            return NoContent();

        return Ok(register);
    }

    /// <summary>
    /// Admin-only: generates a 6-character link code that an already-activated
    /// device can redeem to bind itself to this register. Code lifetime is 30
    /// minutes. Rejected with 400 if the register is currently linked.
    /// </summary>
    [HttpPost("registers/{id}/generate-link-code")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(GenerateLinkCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GenerateLinkCode(int id)
    {
        var response = await _cashRegisterService.GenerateLinkCodeAsync(id, BranchId);
        return Ok(response);
    }

    /// <summary>
    /// Device-authenticated: redeems a previously-generated link code to bind
    /// this device to the register encoded by the code. The device identity is
    /// pulled from the JWT (<c>deviceId</c> + <c>branchId</c> claims) — the
    /// body intentionally does not carry device credentials so a forged
    /// payload cannot impersonate another terminal.
    /// </summary>
    [HttpPost("registers/redeem-link-code")]
    [Authorize] // Mode B: any authenticated identity, but BaseApiController.DeviceId rejects non-device tokens
    [ProducesResponseType(typeof(CashRegisterDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RedeemLinkCode([FromBody] RedeemLinkCodeRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var register = await _cashRegisterService.RedeemLinkCodeAsync(request.Code, DeviceId, BranchId);
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
    [ProducesResponseType(typeof(CashRegisterSessionDto), StatusCodes.Status200OK)]
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
    /// The opener identity is read from the JWT, not the body.
    /// </summary>
    [HttpPost("session/open")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(CashRegisterSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> OpenSession([FromBody] OpenSessionRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var session = await _cashRegisterService.OpenSessionAsync(BranchId, UserId, request);
        return Ok(session);
    }

    /// <summary>
    /// Closes the current open session. If registerId is provided, closes by register;
    /// otherwise closes by branch (legacy). The closer identity is read from the JWT.
    /// </summary>
    [HttpPost("session/close")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(CashRegisterSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CloseSession(
        [FromBody] CloseSessionRequest request,
        [FromQuery] int? registerId = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var session = await _cashRegisterService.CloseSessionAsync(BranchId, UserId, request, registerId);
        return Ok(session);
    }

    /// <summary>
    /// Adds a movement to the open session. If registerId is provided, targets that register;
    /// otherwise targets the branch session (legacy). The author identity is read from the JWT.
    /// </summary>
    [HttpPost("movement")]
    [Authorize(Roles = "Owner,Manager,Cashier")]
    [ProducesResponseType(typeof(CashMovementDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddMovement(
        [FromBody] AddMovementRequest request,
        [FromQuery] int? registerId = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var movement = await _cashRegisterService.AddMovementAsync(BranchId, UserId, request, registerId);
        return Ok(movement);
    }

    /// <summary>
    /// Gets session history for a date range (all registers for the branch).
    /// </summary>
    [HttpGet("history")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(IEnumerable<CashRegisterSessionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to)
    {
        var sessions = await _cashRegisterService.GetHistoryAsync(BranchId, from, to);
        return Ok(sessions);
    }

    #endregion
}

/// <summary>
/// Request body for <c>POST /api/CashRegister/registers/redeem-link-code</c>.
/// Carries only the code — device identity is pulled from the JWT.
/// </summary>
public class RedeemLinkCodeRequest
{
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = null!;
}
