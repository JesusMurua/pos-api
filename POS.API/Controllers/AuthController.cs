using Microsoft.AspNetCore.Mvc;
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
}
