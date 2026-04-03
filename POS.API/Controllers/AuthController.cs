using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using POS.API.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for authentication.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Authenticates an owner by email and password.
    /// </summary>
    /// <param name="request">Email and password credentials.</param>
    /// <returns>JWT token and user info.</returns>
    /// <response code="200">Returns the JWT token and user info.</response>
    /// <response code="400">If credentials are invalid.</response>
    [HttpPost("email-login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> EmailLogin([FromBody] EmailLoginRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var response = await _authService.EmailLoginAsync(request.Email, request.Password);
        return Ok(response);
    }

    /// <summary>
    /// Authenticates a staff member by branch PIN.
    /// </summary>
    /// <param name="request">Branch ID and PIN.</param>
    /// <returns>JWT token and user info.</returns>
    /// <response code="200">Returns the JWT token and user info.</response>
    /// <response code="400">If PIN is invalid.</response>
    [HttpPost("pin-login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PinLogin([FromBody] PinLoginRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var response = await _authService.PinLoginAsync(request.BranchId, request.Pin);
        return Ok(response);
    }

    /// <summary>
    /// Switches the authenticated user to a different branch and returns a new JWT.
    /// </summary>
    /// <param name="request">The target branch identifier.</param>
    /// <returns>A new JWT token and user info for the selected branch.</returns>
    /// <response code="200">Returns the new JWT token and user info.</response>
    /// <response code="401">If the user is not authorized to access the branch.</response>
    /// <response code="404">If the user or branch is not found.</response>
    [HttpPost("switch-branch")]
    [Authorize]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SwitchBranch([FromBody] SwitchBranchRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            return Unauthorized();

        var response = await _authService.SwitchBranchAsync(userId, request.BranchId);
        return Ok(response);
    }

    /// <summary>
    /// Registers a new business with owner account.
    /// </summary>
    /// <param name="request">Registration data.</param>
    /// <returns>JWT token and user info.</returns>
    /// <response code="200">Returns the JWT token and user info.</response>
    /// <response code="400">If validation fails.</response>
    /// <response code="409">If email already exists.</response>
    [HttpPost("register")]
    [EnableRateLimiting("RegistrationPolicy")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register([FromBody] RegisterApiRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var response = await _authService.RegisterAsync(new RegisterRequest
            {
                BusinessName = request.BusinessName,
                OwnerName = request.OwnerName,
                Email = request.Email,
                Password = request.Password,
                BusinessType = request.BusinessType,
                PlanType = request.PlanType
            });
            return Ok(response);
        }
        catch (POS.Domain.Exceptions.ValidationException ex) when (ex.Message == "EMAIL_ALREADY_EXISTS")
        {
            return Conflict(new { message = "Email already registered" });
        }
    }
}
