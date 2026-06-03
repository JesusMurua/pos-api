using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using POS.Domain.DTOs.Pos;
using POS.Domain.Enums;
using POS.Domain.Exceptions;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// POS-flow-level orchestration endpoints. Lives alongside the existing
/// device / cash-register / orders surface but groups the operations that
/// span more than one subsystem so the SPA does not have to chain calls
/// that can leave inconsistent state.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class PosController : BaseApiController
{
    private readonly ICashierSessionService _cashierSession;

    public PosController(ICashierSessionService cashierSession)
    {
        _cashierSession = cashierSession;
    }

    /// <summary>
    /// Single atomic call that the SPA invokes when the Owner / Manager
    /// lands on <c>/pos/sell</c>. Registers (or recovers) the browser's
    /// device row, creates (or takes over) the named cash register, and
    /// links them inside one transaction. Replaces the prior 3-call FE
    /// chain that produced an orphan register in prod when the
    /// device-register step never ran.
    /// </summary>
    /// <response code="200">Device + register linked. Inspect <c>outcome</c> to render the right toast.</response>
    /// <response code="400">Invalid uuid, inactive register, or invalid mode.</response>
    /// <response code="401">Missing / invalid Bearer JWT.</response>
    /// <response code="403">Manager calling with a <c>branchIdOverride</c> they are not assigned to.</response>
    /// <response code="404">Branch override is unknown or cross-tenant.</response>
    /// <response code="409">Register exists with a different device that has an open session and the caller did not set <c>force=true</c>.</response>
    /// <response code="429">Rate limit exceeded.</response>
    [HttpPost("initialize-cashier-session")]
    [Authorize(Roles = "Owner,Manager")]
    [EnableRateLimiting("PosInitializePolicy")]
    [ProducesResponseType(typeof(InitializeCashierSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> InitializeCashierSession([FromBody] InitializeCashierSessionRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        // Resolve the calling user's role id from the JWT Role claim. The
        // claim ships as the PascalCase code ("Owner" / "Manager" / ...);
        // map it to the int the service expects.
        if (!Enum.TryParse<UserRole>(UserRole, out var role))
            return Unauthorized();

        try
        {
            var response = await _cashierSession.InitializeAsync(
                businessId: BusinessId,
                claimBranchId: BranchId,
                userId: UserId,
                userRoleId: (int)role,
                request: request);

            return Ok(response);
        }
        catch (SessionOpenOnOtherDeviceException ex)
        {
            return Conflict(new
            {
                error = "session_open_on_other_device",
                existingRegisterId = ex.ExistingRegisterId,
                openSessionId = ex.OpenSessionId,
                message = $"La caja '{ex.RegisterName}' tiene un turno abierto en otro dispositivo. " +
                          "Confirma takeover para reclamarla."
            });
        }
        catch (NotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
