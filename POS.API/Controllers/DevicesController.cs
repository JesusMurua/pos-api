using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.DTOs.Device;
using POS.Domain.Exceptions;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for physical device registration and management.
/// </summary>
[Route("api/[controller]")]
public class DevicesController : BaseApiController
{
    private readonly IDeviceService _deviceService;

    public DevicesController(IDeviceService deviceService)
    {
        _deviceService = deviceService;
    }

    /// <summary>
    /// Registers a new physical device or updates an existing one.
    /// </summary>
    /// <param name="request">Device UUID, branch, mode, and optional name.</param>
    /// <returns>The registered device configuration.</returns>
    /// <response code="200">Device registered or updated successfully.</response>
    /// <response code="400">If mode is invalid.</response>
    [HttpPost("register")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Register([FromBody] DeviceRegistrationRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var response = await _deviceService.RegisterOrUpdateDeviceAsync(request);
            return Ok(response);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Updates the heartbeat timestamp for a device.
    /// </summary>
    /// <param name="uuid">The device UUID.</param>
    /// <response code="204">Heartbeat updated.</response>
    /// <response code="404">Device not found.</response>
    [HttpPut("heartbeat/{uuid}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Heartbeat(string uuid)
    {
        try
        {
            await _deviceService.UpdateHeartbeatAsync(uuid);
            return NoContent();
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Returns the current configuration for a device so the frontend can self-configure on load.
    /// </summary>
    /// <param name="uuid">The device UUID.</param>
    /// <returns>Device mode, branch, and active status.</returns>
    /// <response code="200">Returns device configuration.</response>
    /// <response code="404">Device not registered.</response>
    [HttpGet("validate/{uuid}")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Validate(string uuid)
    {
        var device = await _deviceService.GetByUuidAsync(uuid);
        if (device == null)
            return NotFound("Device not registered");

        return Ok(device);
    }
}
