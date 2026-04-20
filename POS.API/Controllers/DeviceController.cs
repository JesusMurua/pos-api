using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for device setup and activation codes.
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class DeviceController : BaseApiController
{
    private readonly IDeviceService _deviceService;

    public DeviceController(IDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    /// <summary>
    /// Generates a 6-digit activation code for device setup.
    /// </summary>
    /// <param name="request">Branch and mode for the device.</param>
    /// <returns>The generated code and expiration time.</returns>
    /// <response code="200">Returns the activation code.</response>
    /// <response code="400">If the mode is invalid.</response>
    [HttpPost("generate-code")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(GenerateCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GenerateCode([FromBody] GenerateCodeRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var response = await _deviceService.GenerateActivationCodeAsync(
            BusinessId, request.BranchId, request.Mode, request.Name, UserId);
        return Ok(response);
    }

    /// <summary>
    /// Activates a device using a 6-digit code.
    /// </summary>
    /// <param name="request">The activation code.</param>
    /// <returns>Business and branch info for the device.</returns>
    /// <response code="200">Returns device configuration.</response>
    /// <response code="400">If the code is invalid, used, or expired.</response>
    [HttpPost("activate")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ActivateDeviceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Activate([FromBody] ActivateDeviceRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var response = await _deviceService.ValidateActivationCodeAsync(request.Code);
        return Ok(response);
    }

    /// <summary>
    /// Sets up a device using Owner email credentials.
    /// </summary>
    /// <param name="request">Owner email and password.</param>
    /// <returns>Business info and available branches.</returns>
    /// <response code="200">Returns business and branches.</response>
    /// <response code="400">If credentials are invalid or user is not Owner.</response>
    [HttpPost("setup")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(DeviceSetupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Setup([FromBody] DeviceSetupRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var response = await _deviceService.SetupWithEmailAsync(request.Email, request.Password);
        return Ok(response);
    }
}

/// <summary>
/// Request body for generating an activation code. The code acts as a pre-configured
/// "package" — the Admin chooses Branch, Mode, and Name at generation time so the
/// fresh terminal auto-configures without further prompts.
/// </summary>
public class GenerateCodeRequest
{
    [Required]
    public int BranchId { get; set; }

    [Required]
    public string Mode { get; set; } = null!;

    /// <summary>
    /// Human-readable device label chosen by the Admin (e.g. "Caja 1", "KDS Cocina").
    /// Copied into <c>Device.Name</c> on registration so the operator never has to
    /// re-enter it at the terminal.
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = null!;
}

/// <summary>
/// Request body for activating a device with a code.
/// </summary>
public class ActivateDeviceRequest
{
    [Required]
    [StringLength(6, MinimumLength = 6)]
    public string Code { get; set; } = null!;
}

/// <summary>
/// Request body for device setup with email credentials.
/// </summary>
public class DeviceSetupRequest
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = null!;

    [Required]
    public string Password { get; set; } = null!;
}
