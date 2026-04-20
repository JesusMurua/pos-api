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

    /// <summary>
    /// Lists all devices for the caller's business, optionally narrowed to a
    /// specific branch. Scoped by the caller's <c>BusinessId</c> claim —
    /// cross-business ids yield an empty array, never a 403.
    /// </summary>
    /// <param name="branchId">Optional branch id to filter by.</param>
    /// <returns>Array of device projections with <c>BranchName</c> included.</returns>
    /// <response code="200">Returns the list of devices.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IReadOnlyList<DeviceListItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int? branchId = null)
    {
        var devices = await _deviceService.ListForBusinessAsync(BusinessId, branchId);
        return Ok(devices);
    }

    /// <summary>
    /// Flips the <c>IsActive</c> flag on a device and invalidates the auth
    /// cache so the revocation propagates on the next device request.
    /// </summary>
    /// <param name="id">Device id.</param>
    /// <response code="200">Returns the new active status.</response>
    /// <response code="404">Device does not exist or belongs to another business.</response>
    [HttpPatch("{id}/toggle-active")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(ToggleActiveResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ToggleActive(int id)
    {
        try
        {
            var result = await _deviceService.ToggleActiveAsync(id, BusinessId);
            return Ok(result);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Partial update of a device. Accepts any subset of <c>{ name, branchId }</c>.
    /// An empty body is rejected with 400. Cross-tenant <c>BranchId</c> values
    /// are rejected with 400 (branch id is body input); cross-tenant device ids
    /// are rejected with 404 (device id is an enumerable path param).
    /// </summary>
    /// <param name="id">Device id.</param>
    /// <param name="request">Partial update payload.</param>
    /// <returns>The updated device in list-projection shape.</returns>
    /// <response code="200">Returns the updated device.</response>
    /// <response code="400">If the body is empty or validation fails.</response>
    /// <response code="404">Device does not exist or belongs to another business.</response>
    [HttpPatch("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(DeviceListItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateDeviceRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            var updated = await _deviceService.UpdateDeviceAsync(id, BusinessId, request);
            return Ok(updated);
        }
        catch (NotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
