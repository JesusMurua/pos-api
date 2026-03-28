using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using POS.Domain.Models;
using POS.Services.IService;

namespace POS.API.Controllers;

/// <summary>
/// Controller for managing zones within a branch.
/// </summary>
[Route("api/[controller]")]
[Authorize]
public class ZoneController : BaseApiController
{
    private readonly IZoneService _zoneService;

    public ZoneController(IZoneService zoneService)
    {
        _zoneService = zoneService;
    }

    /// <summary>
    /// Gets all zones for the current branch ordered by SortOrder.
    /// </summary>
    /// <returns>A list of zones.</returns>
    /// <response code="200">Returns the list of zones.</response>
    [HttpGet]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(IEnumerable<Zone>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetByBranch()
    {
        var zones = await _zoneService.GetByBranchAsync(BranchId);
        return Ok(zones);
    }

    /// <summary>
    /// Creates a new zone in the current branch.
    /// </summary>
    /// <param name="zone">The zone data.</param>
    /// <returns>The created zone.</returns>
    /// <response code="200">Returns the created zone.</response>
    /// <response code="400">If validation fails.</response>
    [HttpPost]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(Zone), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] Zone zone)
    {
        zone.BranchId = BranchId;
        ModelState.Remove("Branch");
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var created = await _zoneService.CreateAsync(BranchId, zone);
        return Ok(created);
    }

    /// <summary>
    /// Updates an existing zone.
    /// </summary>
    /// <param name="id">The zone identifier.</param>
    /// <param name="zone">The updated zone data.</param>
    /// <returns>The updated zone.</returns>
    /// <response code="200">Returns the updated zone.</response>
    /// <response code="404">If the zone is not found.</response>
    [HttpPut("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(Zone), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(int id, [FromBody] Zone zone)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var updated = await _zoneService.UpdateAsync(id, BranchId, zone);
        return Ok(updated);
    }

    /// <summary>
    /// Deletes a zone.
    /// </summary>
    /// <param name="id">The zone identifier.</param>
    /// <returns>Success acknowledgement.</returns>
    /// <response code="200">Zone deleted.</response>
    /// <response code="404">If the zone is not found.</response>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Owner,Manager")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(int id)
    {
        await _zoneService.DeleteAsync(id, BranchId);
        return Ok(new { success = true });
    }
}
