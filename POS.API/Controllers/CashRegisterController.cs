using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing cash register sessions and movements.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[Authorize]
public class CashRegisterController : ControllerBase
{
    private readonly ICashRegisterService _cashRegisterService;

    public CashRegisterController(ICashRegisterService cashRegisterService)
    {
        _cashRegisterService = cashRegisterService;
    }

    /// <summary>
    /// Gets the current open session for a branch.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <returns>The open session or 204 if none exists.</returns>
    /// <response code="200">Returns the open session.</response>
    /// <response code="204">No open session exists.</response>
    [HttpGet("session")]
    [Authorize(Roles = "Owner,Cashier")]
    [ProducesResponseType(typeof(CashRegisterSession), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> GetOpenSession([FromQuery] int branchId)
    {
        var session = await _cashRegisterService.GetOpenSessionAsync(branchId);

        if (session == null)
            return NoContent();

        return Ok(session);
    }

    /// <summary>
    /// Opens a new cash register session.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="request">The session opening data.</param>
    /// <returns>The created session.</returns>
    /// <response code="200">Returns the created session.</response>
    /// <response code="400">If there is already an open session.</response>
    [HttpPost("session/open")]
    [Authorize(Roles = "Owner,Cashier")]
    [ProducesResponseType(typeof(CashRegisterSession), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> OpenSession(
        [FromQuery] int branchId,
        [FromBody] OpenSessionRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var session = await _cashRegisterService.OpenSessionAsync(branchId, request);
        return Ok(session);
    }

    /// <summary>
    /// Closes the current open session.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="request">The session closing data.</param>
    /// <returns>The closed session.</returns>
    /// <response code="200">Returns the closed session.</response>
    /// <response code="404">If no open session exists.</response>
    [HttpPost("session/close")]
    [Authorize(Roles = "Owner,Cashier")]
    [ProducesResponseType(typeof(CashRegisterSession), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CloseSession(
        [FromQuery] int branchId,
        [FromBody] CloseSessionRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var session = await _cashRegisterService.CloseSessionAsync(branchId, request);
        return Ok(session);
    }

    /// <summary>
    /// Adds a movement to the current open session.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="request">The movement data.</param>
    /// <returns>The created movement.</returns>
    /// <response code="200">Returns the created movement.</response>
    /// <response code="404">If no open session exists.</response>
    /// <response code="400">If the request data is invalid.</response>
    [HttpPost("movement")]
    [Authorize(Roles = "Owner,Cashier")]
    [ProducesResponseType(typeof(CashMovement), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AddMovement(
        [FromQuery] int branchId,
        [FromBody] AddMovementRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var movement = await _cashRegisterService.AddMovementAsync(branchId, request);
        return Ok(movement);
    }

    /// <summary>
    /// Gets session history for a date range.
    /// </summary>
    /// <param name="branchId">The branch identifier.</param>
    /// <param name="from">Start date.</param>
    /// <param name="to">End date.</param>
    /// <returns>Sessions within the date range.</returns>
    /// <response code="200">Returns the session history.</response>
    [HttpGet("history")]
    [Authorize(Roles = "Owner")]
    [ProducesResponseType(typeof(IEnumerable<CashRegisterSession>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int branchId,
        [FromQuery] DateTime from,
        [FromQuery] DateTime to)
    {
        var sessions = await _cashRegisterService.GetHistoryAsync(branchId, from, to);
        return Ok(sessions);
    }
}
